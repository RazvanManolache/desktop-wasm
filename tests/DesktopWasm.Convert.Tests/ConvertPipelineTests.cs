using DesktopWasm.Convert;
using DesktopWasm.Wpf;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// End-to-end conversion proof on synthesized generated code: the fixture
/// mimics PresentationBuildTasks output (partial App/Window classes calling
/// Application.LoadComponent with StartupUri/component URIs) with dangling
/// framework references, exactly what the converter must rebind.
/// </summary>
public sealed class ConvertPipelineTests : IDisposable
{
    private const string AppXaml =
        "<Application x:Class=\"Demo.App\" " +
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "StartupUri=\"MainWindow.xaml\"/>";

    private const string MainXaml =
        "<Window x:Class=\"Demo.MainWindow\" Title=\"Converted\" Width=\"400\" Height=\"300\" " +
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:local=\"clr-namespace:Demo\">" +
        "<Grid><TextBlock VerticalAlignment=\"Center\" HorizontalAlignment=\"Center\">Hi</TextBlock>" +
        "<local:FancyBox Text=\"F\" /></Grid>" +
        "</Window>";

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "wpf-convert-" + Guid.NewGuid().ToString("N"));
    private readonly Application? _previousApp = Application.Current;

    public void Dispose()
    {
        Application.Current = _previousApp;
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public void MapTargetsResolveInEngine()
    {
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversCommandContractFromAnyScope()
    {
        // System.Windows.Input.ICommand moved out of the WPF assemblies
        // (System.ObjectModel on modern TFMs, netstandard for portable code,
        // PresentationCore on .NET Framework): every scope maps onto the
        // engine command contract. Unrelated moved types stay unmapped.
        Assert.NotNull(WpfTypeMap.MapType("System.ObjectModel", "System.Windows.Input", "ICommand"));
        Assert.NotNull(WpfTypeMap.MapType("netstandard", "System.Windows.Input", "ICommand"));
        Assert.NotNull(WpfTypeMap.MapType("PresentationCore", "System.Windows.Input", "ICommand"));
        Assert.NotNull(typeof(Wpf.Window).Assembly.GetType("DesktopWasm.Wpf.ICommand"));
        Assert.Null(WpfTypeMap.MapType("System.ObjectModel", "System.ObjectModel", "ObservableCollection`1"));
    }

    [Fact]
    public void MapCoversItemsCluster()
    {
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "ItemsControl"),
            ("PresentationFramework", "System.Windows.Controls", "Selector"),
            ("PresentationFramework", "System.Windows.Controls", "HeaderedItemsControl"),
            ("PresentationFramework", "System.Windows.Controls", "ComboBox"),
            ("PresentationFramework", "System.Windows.Controls", "ComboBoxItem"),
            ("PresentationFramework", "System.Windows.Controls", "ListView"),
            ("PresentationFramework", "System.Windows.Controls", "ListViewItem"),
            ("PresentationFramework", "System.Windows.Controls", "Orientation"),
            ("PresentationFramework", "System.Windows.Controls", "SelectionMode"),
            ("PresentationFramework", "System.Windows.Data", "Binding"),
            ("PresentationFramework", "System.Windows.Data", "BindingBase"),
            ("PresentationFramework", "System.Windows.Data", "IValueConverter"),
            ("PresentationFramework", "System.Windows.Data", "UpdateSourceTrigger"),
            ("PresentationFramework", "System.Windows.Data", "CollectionView"),
            ("PresentationFramework", "System.Windows.Data", "ListCollectionView"),
            ("PresentationFramework", "System.Windows.Data", "PropertyGroupDescription"),
            ("PresentationFramework", "System.Windows", "ResizeMode"),
            ("PresentationFramework", "System.Windows", "WindowStyle"),
            ("PresentationFramework", "System.Windows", "WindowState"),
            ("PresentationFramework", "System.Windows", "SystemParameters"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversSliderSlice()
    {
        // Gallery BasicInput/SliderPage + UserDashboard age slider references.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls.Primitives", "RangeBase"),
            ("PresentationFramework", "System.Windows.Controls", "Slider"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "TickPlacement"),
            ("PresentationFramework", "System.Windows", "RoutedPropertyChangedEventArgs`1"),
            ("PresentationFramework", "System.Windows", "RoutedPropertyChangedEventHandler`1"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversSizeChangedEllipseSlice()
    {
        // Gallery TileGallery responsive layout + UserDashboard avatar refs.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "SizeChangedEventArgs"),
            ("PresentationFramework", "System.Windows", "SizeChangedEventHandler"),
            ("PresentationFramework", "System.Windows.Shapes", "Ellipse"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversScrollChangedSlice()
    {
        // ScreenToGif ZoomBox/ImageViewer/ScrollSynchronizer references:
        // ScrollViewer.ScrollChanged handlers read offset/extent/viewport
        // transitions through ScrollChangedEventArgs.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "ScrollViewer"),
            ("PresentationFramework", "System.Windows.Controls", "ScrollChangedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "ScrollChangedEventHandler"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversValidationHyperlinkSlice()
    {
        // Gallery TextBoxPage validation + Text/Hyperlink/Clipboard navigation refs.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "ValidationRule"),
            ("PresentationFramework", "System.Windows.Controls", "ValidationResult"),
            ("PresentationFramework", "System.Windows.Documents", "Hyperlink"),
            ("PresentationFramework", "System.Windows.Navigation", "RequestNavigateEventArgs"),
            ("PresentationFramework", "System.Windows.Navigation", "RequestNavigateEventHandler"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversFileDialogClipboardSlice()
    {
        // Gallery FileAndFolderDialogsPage + ClipboardPage references.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows", "Clipboard"),
            ("PresentationFramework", "Microsoft.Win32", "CommonDialog"),
            ("PresentationFramework", "Microsoft.Win32", "FileDialog"),
            ("PresentationFramework", "Microsoft.Win32", "CommonItemDialog"),
            ("PresentationFramework", "Microsoft.Win32", "OpenFileDialog"),
            ("PresentationFramework", "Microsoft.Win32", "SaveFileDialog"),
            ("PresentationFramework", "Microsoft.Win32", "OpenFolderDialog"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversNavigationEventsSlice()
    {
        // Gallery MainWindow/RootContentFrame_Navigated references.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Navigation", "NavigationEventArgs"),
            ("PresentationFramework", "System.Windows.Navigation", "NavigatedEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "Frame"),
            ("PresentationFramework", "System.Windows.Controls", "Page"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversAutomationSystemEventsSlice()
    {
        // Gallery MainWindow/ColorTile/ControlExample/IconDataField copy
        // handlers (automation peers + notification enums), MainWindow/
        // HeaderTile preference subscriptions (SystemEvents), and the
        // Hosting EventLog logger closure (EventLog/EventInstance/EntryType).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Automation.Peers", "AutomationPeer"),
            ("PresentationCore", "System.Windows.Automation.Peers", "UIElementAutomationPeer"),
            ("UIAutomationTypes", "System.Windows.Automation", "AutomationNotificationKind"),
            ("UIAutomationTypes", "System.Windows.Automation", "AutomationNotificationProcessing"),
            ("PresentationCore", "System.Windows.Automation", "AutomationProperties"),
            ("PresentationCore", "System.Windows.Automation", "AutomationHeadingLevel"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SystemEvents"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventHandler"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventArgs"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceCategory"),
            ("System.Diagnostics.EventLog", "System.Diagnostics", "EventLog"),
            ("System.Diagnostics.EventLog", "System.Diagnostics", "EventInstance"),
            ("System.Diagnostics.EventLog", "System.Diagnostics", "EventLogEntryType"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversChromePackSlice()
    {
        // Gallery MainWindow custom chrome + ControlExample pack-stream references.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Resources", "StreamResourceInfo"),
            ("PresentationFramework", "System.Windows.Shell", "WindowChrome"),
            ("PresentationFramework", "System.Windows.Shell", "NonClientFrameEdges"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversInkDocumentSlice()
    {
        // ScreenToGif ink/stylus + FlowDocument clusters (DrawingCanvas board
        // inking, BoardRecorderViewModel DrawingAttributes/EditingMode,
        // History Content documents, DownloadDialog paragraphs): every live
        // referenced type must resolve onto the engine.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Ink", "StylusShape"),
            ("PresentationCore", "System.Windows.Ink", "RectangleStylusShape"),
            ("PresentationCore", "System.Windows.Ink", "EllipseStylusShape"),
            ("PresentationCore", "System.Windows.Ink", "StylusTip"),
            ("PresentationCore", "System.Windows.Ink", "ApplicationGesture"),
            ("PresentationCore", "System.Windows.Ink", "DrawingAttributes"),
            ("PresentationCore", "System.Windows.Ink", "Stroke"),
            ("PresentationCore", "System.Windows.Ink", "StrokeCollection"),
            ("PresentationCore", "System.Windows.Input", "StylusPoint"),
            ("PresentationCore", "System.Windows.Input", "StylusPointCollection"),
            ("PresentationCore", "System.Windows.Input", "StylusButtonEventArgs"),
            ("PresentationCore", "System.Windows.Input", "StylusButtonEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "InkCanvas"),
            ("PresentationFramework", "System.Windows.Controls", "InkCanvasEditingMode"),
            ("PresentationFramework", "System.Windows.Controls", "InkCanvasStrokeCollectedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "InkCanvasStrokeCollectedEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "FlowDocumentScrollViewer"),
            ("PresentationFramework", "System.Windows.Documents", "TextElement"),
            ("PresentationFramework", "System.Windows.Documents", "Block"),
            ("PresentationFramework", "System.Windows.Documents", "Paragraph"),
            ("PresentationFramework", "System.Windows.Documents", "Inline"),
            ("PresentationFramework", "System.Windows.Documents", "InlineCollection"),
            ("PresentationFramework", "System.Windows.Documents", "BlockCollection"),
            ("PresentationFramework", "System.Windows.Documents", "TextElementCollection`1"),
            ("PresentationFramework", "System.Windows.Documents", "FlowDocument"),
            ("PresentationFramework", "System.Windows", "FrameworkContentElement"),
            ("PresentationCore", "System.Windows", "FontStretch"),
            ("PresentationCore", "System.Windows", "FlowDirection"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversHelloWorldShape()
    {
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows", "Application"));
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows", "Window"));
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Markup", "IComponentConnector"));
        // Engine Visual exists (Step 42); unmapped types still yield precise gaps.
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Media", "Visual"));
    }

    [Fact]
    public void MapCoversGeometrySlice()
    {
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Shapes", "Path"),
            ("PresentationFramework", "System.Windows.Shapes", "Rectangle"),
            ("PresentationFramework", "System.Windows.Controls", "Viewbox"),
            ("PresentationFramework", "System.Windows.Controls", "ContentPresenter"),
            ("PresentationFramework", "System.Windows.Documents", "Span"),
            ("PresentationFramework", "System.Windows.Documents", "Run"),
            ("PresentationFramework", "System.Windows.Documents", "LineBreak"),
            ("PresentationFramework", "System.Windows.Navigation", "NavigationWindow"),
            ("PresentationFramework", "System.Windows", "Trigger"),
            ("PresentationFramework", "System.Windows", "EventTrigger"),
            ("PresentationFramework", "System.Windows", "TextWrapping"),
            ("PresentationFramework", "System.Windows.Controls", "ScrollBarVisibility"),
            ("PresentationCore", "System.Windows.Media", "Geometry"),
            ("PresentationCore", "System.Windows.Media", "LineGeometry"),
            ("PresentationCore", "System.Windows.Media", "RectangleGeometry"),
            ("PresentationCore", "System.Windows.Media", "EllipseGeometry"),
            ("PresentationCore", "System.Windows.Media", "GeometryGroup"),
            ("PresentationCore", "System.Windows.Media", "PathGeometry"),
            ("PresentationCore", "System.Windows.Media", "PathFigure"),
            ("PresentationCore", "System.Windows.Media", "BezierSegment"),
            ("PresentationCore", "System.Windows.Media", "ArcSegment"),
            ("PresentationCore", "System.Windows.Media", "CombinedGeometry"),
            ("PresentationCore", "System.Windows.Media", "GeometryCombineMode"),
            ("PresentationCore", "System.Windows.Media", "FillRule"),
            ("PresentationCore", "System.Windows.Media", "Pen"),
            ("PresentationCore", "System.Windows.Media", "DrawingGroup"),
            ("PresentationCore", "System.Windows.Media", "GeometryDrawing"),
            ("PresentationCore", "System.Windows.Media", "TileMode"),
            ("PresentationCore", "System.Windows.Media", "BrushMappingMode"),
            ("PresentationCore", "System.Windows.Media", "DrawingBrush"),
            ("PresentationCore", "System.Windows.Media", "VisualBrush"),
            ("PresentationCore", "System.Windows.Media", "GradientStop"),
            ("PresentationCore", "System.Windows.Media", "GradientStopCollection"),
            ("PresentationCore", "System.Windows.Media", "LinearGradientBrush"),
            ("PresentationCore", "System.Windows.Media", "RadialGradientBrush"),
            ("PresentationFramework", "System.Windows.Media.Animation", "BeginStoryboard"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversShapeStreamGeometrySlice()
    {
        // ScreenToGif geometry/drawing prerequisites (Arrow : Shape with
        // StreamGeometry, KGySoft poly segments, pen/dash/color enums,
        // metadata options and callbacks, DrawingImage).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Shapes", "Shape"),
            ("PresentationFramework", "System.Windows.Shapes", "Line"),
            ("PresentationFramework", "System.Windows.Shapes", "Polyline"),
            ("PresentationCore", "System.Windows.Media", "StreamGeometry"),
            ("PresentationCore", "System.Windows.Media", "StreamGeometryContext"),
            ("PresentationCore", "System.Windows.Media", "PolyLineSegment"),
            ("PresentationCore", "System.Windows.Media", "PolyBezierSegment"),
            ("PresentationCore", "System.Windows.Media", "PolyQuadraticBezierSegment"),
            ("PresentationCore", "System.Windows.Media", "PenLineCap"),
            ("PresentationCore", "System.Windows.Media", "PenLineJoin"),
            ("PresentationCore", "System.Windows.Media", "DashStyle"),
            ("PresentationCore", "System.Windows.Media", "DashStyles"),
            ("PresentationCore", "System.Windows.Media", "ColorInterpolationMode"),
            ("PresentationCore", "System.Windows.Media", "DoubleCollection"),
            ("PresentationCore", "System.Windows.Media", "PointCollection"),
            ("PresentationCore", "System.Windows.Media", "DrawingImage"),
            ("PresentationFramework", "System.Windows", "FrameworkPropertyMetadataOptions"),
            ("PresentationFramework", "System.Windows", "UIPropertyMetadata"),
            ("WindowsBase", "System.Windows", "DependencyPropertyChangedEventHandler"),
            ("WindowsBase", "System.Windows", "CoerceValueCallback"),
            ("WindowsBase", "System.Windows", "ValidateValueCallback"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        // Nested collection enumerators map allowlist-first (declaring type)
        // with an engine RuntimeType fallback (nested name).
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "PathFigureCollection", ["Enumerator"]));
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "PathSegmentCollection", ["Enumerator"]));
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "GradientStopCollection", ["Enumerator"]));
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "PointCollection", ["Enumerator"]));
        Assert.Null(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "PathFigureCollection", ["Missing"]));
        Assert.Null(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "MediaPlayer", ["Enumerator"]));
    }

    [Fact]
    public void MapCoversAdornerBindingOpsSlice()
    {
        // ScreenToGif adorner/binding leftovers: AdornedControl hosts a
        // FrameworkElementAdorner through AdornerLayer.Add/Remove over an
        // AdornerDecorator; Editor/TimeBox/DynamicResourceBinding drive
        // BindingOperations.SetBinding, GetBindingExpression and the
        // DependencyProperty PropertyPath shape through the binding engine.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "Decorator"),
            ("PresentationFramework", "System.Windows.Controls", "AdornerDecorator"),
            ("PresentationFramework", "System.Windows.Documents", "Adorner"),
            ("PresentationFramework", "System.Windows.Documents", "AdornerLayer"),
            ("PresentationFramework", "System.Windows", "PropertyPath"),
            ("PresentationFramework", "System.Windows.Data", "BindingOperations"),
            ("PresentationFramework", "System.Windows.Data", "BindingExpressionBase"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversPrimitivesSlice()
    {
        // ScreenToGif primitives: CroppingAdorner/ElementAdorner drag Thumb
        // handles with DragDelta/DragStarted payloads, UpDown spinners
        // subclass RepeatButton, SplitButton/templates open Popup panels with
        // PlacementMode anchors. DragCompleted is not a live gap but ships in
        // the same contract (CancelDrag/cancel path).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls.Primitives", "Thumb"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragDeltaEventArgs"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragDeltaEventHandler"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragStartedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragStartedEventHandler"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragCompletedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DragCompletedEventHandler"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "RepeatButton"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "Popup"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "PlacementMode"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversTabProgressTooltipSlice()
    {
        // ScreenToGif tab/progress/tooltip leftovers: HideableTabControl /
        // AwareTabItem need TabControl/TabPanel/TabItem selection plus
        // header/content switching, Circular/ExtendedProgressBar need
        // ProgressBar range/value paint, and HeaderedTooltip plus the
        // ToolTipService-attached hints need ToolTip + ToolTipService.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "TabControl"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "TabPanel"),
            ("PresentationFramework", "System.Windows.Controls", "TabItem"),
            ("PresentationFramework", "System.Windows.Controls", "ProgressBar"),
            ("PresentationFramework", "System.Windows.Controls", "ToolTip"),
            ("PresentationFramework", "System.Windows.Controls", "ToolTipService"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversGroupBoxSlice()
    {
        // Gallery GroupBoxPage (slice-32): the Layout detail page frames
        // its Name/Gender form in a GroupBox ("User Information" header);
        // the converter maps it onto the engine GroupBox by the default
        // System.Windows.* namespace rule, so the converted assembly keeps
        // converting with zero gaps and the page inflates from staging.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "GroupBox"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversCalendarSlice()
    {
        // Gallery CalendarPage (slice-37): the DateAndTime detail page hosts
        // a bare <Calendar/>. All eight names map onto the engine by the
        // default System.Windows.* namespace rule, so the converted assembly
        // keeps converting with zero gaps and the page inflates from staging
        // (the loader used to reject the element as unknown).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "Calendar"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarSelectionMode"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarMode"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarDateRange"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarBlackoutDatesCollection"),
            ("PresentationFramework", "System.Windows.Controls", "SelectedDatesCollection"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarDateChangedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "CalendarModeChangedEventArgs"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversDatePickerSlice()
    {
        // Gallery DatePickerPage (slice-38): the DateAndTime detail page
        // hosts a bare <DatePicker/>. Both names map onto the engine by the
        // default System.Windows.* namespace rule, so the converted assembly
        // keeps converting with zero gaps and the page inflates from staging
        // (the loader used to reject the element as unknown).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "DatePicker"),
            ("PresentationFramework", "System.Windows.Controls", "DatePickerDateValidationErrorEventArgs"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversRichTextSlice()
    {
        // Gallery RichTextEditPage (slice-39): the Text detail page hosts a
        // bare <RichTextBox/>. The name maps onto the engine by the default
        // System.Windows.* namespace rule, so the converted assembly keeps
        // converting with zero gaps and the page inflates from staging (the
        // loader used to reject the element as unknown).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "RichTextBox"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversMenuSlice()
    {
        // Gallery MenuPage (slice-36): the Navigation detail page drives a
        // real Menu/MenuItem tree with separators, a style EventSetter and
        // icon headers. All four names map onto the engine by the default
        // System.Windows.* namespace rule, so the converted assembly keeps
        // converting with zero gaps and the page inflates from staging (the
        // loader used to reject the EventSetter, the separators and the
        // visual headers).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "Menu"),
            ("PresentationFramework", "System.Windows.Controls", "MenuItem"),
            ("PresentationFramework", "System.Windows.Controls", "Separator"),
            ("PresentationFramework", "System.Windows", "EventSetter"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversGridShowGridLinesSlice()
    {
        // Gallery GridPage (slice-34): the 3x3 example sets
        // ShowGridLines="True"; the Grid type itself already maps onto the
        // engine Grid by the default System.Windows.* namespace rule, so the
        // converted assembly keeps converting with zero gaps and the page
        // inflates from staging (the loader used to reject the attribute).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "Grid"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversTextFontDpiSlice()
    {
        // ScreenToGif text/font/DPI cluster: TextPath + Util/Other build
        // FormattedText off a resolved Typeface with session pixels-per-dip,
        // StorageSettings paints TextDecoration strikethrough runs, the
        // FontToSupportedGliph converter filters through
        // TryGetGlyphTypeface, WebcamControl/UserSettings query
        // DesignerProperties, TextPath registrations default from
        // SystemFonts, and recorder/editor/region windows rescale off
        // DpiChanged/NewDpi/PixelsPerInchX.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media", "Typeface"),
            ("PresentationCore", "System.Windows.Media", "GlyphTypeface"),
            ("PresentationCore", "System.Windows.Media", "FormattedText"),
            ("PresentationCore", "System.Windows", "DpiScale"),
            ("PresentationCore", "System.Windows", "DpiChangedEventArgs"),
            ("PresentationCore", "System.Windows", "DpiChangedEventHandler"),
            ("PresentationCore", "System.Windows.Media", "NumberSubstitution"),
            ("PresentationCore", "System.Windows.Media", "NumberCultureSource"),
            ("PresentationCore", "System.Windows.Media", "NumberSubstitutionMethod"),
            ("WindowsBase", "System.Windows.Media", "TextFormattingMode"),
            ("PresentationCore", "System.Windows", "TextDecoration"),
            ("PresentationCore", "System.Windows", "TextDecorationCollection"),
            ("PresentationCore", "System.Windows", "TextDecorationLocation"),
            ("PresentationCore", "System.Windows", "TextDecorationUnit"),
            ("PresentationCore", "System.Windows", "TextDecorations"),
            ("PresentationFramework", "System.ComponentModel", "DesignerProperties"),
            ("PresentationFramework", "System.Windows", "SystemFonts"),
            ("WindowsBase", "System.ComponentModel", "DependencyPropertyDescriptor"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversAnimationClocksSlice()
    {
        // ScreenToGif animation-clock cluster: HideableTabControl show/hide
        // (DoubleAnimation/PowerEase BeginAnimation on Height/Opacity/Margin,
        // ObjectAnimationUsingKeyFrames/DiscreteObjectKeyFrame/KeyTime on
        // Visibility/SelectedItem, ThicknessAnimation on Margin) plus the
        // BrushAnimation contract (AnimationTimeline/AnimationClock,
        // ColorAnimation/ColorAnimationBase, HandoffBehavior).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media.Animation", "PowerEase"),
            ("PresentationCore", "System.Windows.Media.Animation", "AnimationTimeline"),
            ("PresentationCore", "System.Windows.Media.Animation", "ObjectAnimationUsingKeyFrames"),
            ("PresentationCore", "System.Windows.Media.Animation", "ObjectKeyFrameCollection"),
            ("PresentationCore", "System.Windows.Media.Animation", "KeyTime"),
            ("PresentationCore", "System.Windows.Media.Animation", "DiscreteObjectKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "ObjectKeyFrame"),
            ("PresentationFramework", "System.Windows.Media.Animation", "ThicknessAnimation"),
            ("PresentationCore", "System.Windows.Media.Animation", "AnimationClock"),
            ("PresentationCore", "System.Windows.Media.Animation", "ColorAnimation"),
            ("PresentationCore", "System.Windows.Media.Animation", "ColorAnimationBase"),
            ("PresentationCore", "System.Windows.Media.Animation", "HandoffBehavior"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
    }

    [Fact]
    public void MapCoversTemplateInflationSlice()
    {
        // ScreenToGif template cluster: SelectControl Template.FindName parts
        // (UniformGrid StatusControlGrid), TemplateBinding/StretchDirection in
        // Generic.xaml themes, ComboBoxItemTemplateSelector deriving from
        // DataTemplateSelector; Gallery boot-chain PageHeader/BorderlessButton
        // templates (FrameworkTemplate ownership, BasedOn/TemplateBinding).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "FrameworkTemplate"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "UniformGrid"),
            ("PresentationFramework", "System.Windows.Controls", "StretchDirection"),
            ("PresentationFramework", "System.Windows.Controls", "DataTemplateSelector"),
            ("PresentationFramework", "System.Windows", "DataTemplate"),
            ("PresentationFramework", "System.Windows", "ControlTemplate"),
            ("PresentationFramework", "System.Windows", "AttachedPropertyBrowsableForTypeAttribute"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(ControlTemplate).IsSubclassOf(typeof(FrameworkTemplate)));
        Assert.True(typeof(DataTemplate).IsSubclassOf(typeof(FrameworkTemplate)));
    }

    [Fact]
    public void MapCoversDynamicResourceHitTestSlice()
    {
        // ScreenToGif adorner/hit-test cluster: CroppingAdorner/
        // ElementAdorner/ResizingAdorner VisualCollection backing with
        // VisualChildrenCount/GetVisualChild overrides, DrawingCanvas rubber
        // band (HitTest + GeometryHitTestParameters + callbacks), Recorder
        // point picks, SelectControl GetFlattenedPathGeometry tolerance, and
        // ScreenToGif.Util DynamicResourceBinding (DynamicResourceExtension
        // base, markup-extension contract, value-target service).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media", "ToleranceType"),
            ("PresentationCore", "System.Windows.Media", "VisualCollection"),
            ("PresentationCore", "System.Windows.Media", "HitTestResultBehavior"),
            ("PresentationCore", "System.Windows.Media", "HitTestResult"),
            ("PresentationCore", "System.Windows.Media", "HitTestResultCallback"),
            ("PresentationCore", "System.Windows.Media", "GeometryHitTestParameters"),
            ("PresentationCore", "System.Windows.Media", "HitTestFilterCallback"),
            ("PresentationCore", "System.Windows.Media", "HitTestParameters"),
            ("PresentationFramework", "System.Windows", "DynamicResourceExtension"),
            ("System.Xaml", "System.Windows.Markup", "MarkupExtension"),
            ("System.Xaml", "System.Windows.Markup", "IProvideValueTarget"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(PointHitTestResult).IsSubclassOf(typeof(HitTestResult)));
        Assert.True(typeof(GeometryHitTestResult).IsSubclassOf(typeof(HitTestResult)));
        Assert.True(typeof(DynamicResourceExtension).IsSubclassOf(typeof(MarkupExtension)));
    }

    [Fact]
    public void MapCoversDataGridRowSlice()
    {
        // ScreenToGif row/cell/sort cluster: KeyStrokes casts
        // ItemContainerGenerator results to DataGridRow and looks up the
        // DataGridDetailsPresenter per row; DataGridHelper walks
        // DataGridCellsPresenter -> DataGridCell containers, reads
        // DataGridCellInfo/Column/DisplayIndex/GetCellContent, sorts through
        // DataGridColumn.Binding + Items.SortDescriptions, and reaches
        // LogicalTreeHelper.GetParent; ExportPanel groups through
        // PropertyGroupDescription.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "DataGridRow"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridCell"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridCellInfo"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridColumn"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridBoundColumn"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DataGridCellsPresenter"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DataGridDetailsPresenter"),
            ("WindowsBase", "System.ComponentModel", "SortDescription"),
            ("WindowsBase", "System.ComponentModel", "SortDescriptionCollection"),
            ("WindowsBase", "System.ComponentModel", "GroupDescription"),
            ("WindowsBase", "System.ComponentModel", "ListSortDirection"),
            ("PresentationFramework", "System.Windows", "LogicalTreeHelper"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(DataGridTextColumn).IsSubclassOf(typeof(DataGridBoundColumn)));
        Assert.True(typeof(DataGridBoundColumn).IsSubclassOf(typeof(DataGridColumn)));
        Assert.True(typeof(DataGridCell).IsSubclassOf(typeof(ContentControl)));
        Assert.True(typeof(DataGridRow).IsSubclassOf(typeof(ContentControl)));
        Assert.True(typeof(DataGridDetailsPresenter).IsSubclassOf(typeof(ContentControl)));
        Assert.True(typeof(SortDescriptionCollection).IsSubclassOf(typeof(System.Collections.ObjectModel.ObservableCollection<SortDescription>)));
        Assert.Equal(0, (int)ListSortDirection.Ascending);
        Assert.Equal(1, (int)ListSortDirection.Descending);
        Assert.Equal("Name", new SortDescription("Name", ListSortDirection.Ascending).PropertyName);
    }

    [Fact]
    public void MapCoversShellWindowingSlice()
    {
        // ScreenToGif shell/windowing cluster: NewRecorder/Recorder windows
        // declare Window.TaskbarItemInfo with ThumbButtonInfo collections in
        // XAML and drive ProgressState/ProgressValue/Overlay from code;
        // DownloadDialog/Insert/Localization/VideoSource/Options assign
        // SizeToContent; VideoSource splits its columns with a GridSplitter.
        // The SizeToContent gap is attributed to PresentationCore, the
        // shell/taskbar names to System.Windows.Shell, GridSplitter to
        // System.Windows.Controls: each resolves through the default
        // namespace rule onto the same-named engine type. TaskbarItemProgressState's
        // values plus the GridSplitterThumb/GridResize* enums are verified
        // against the .NET 9 runtime PresentationFramework IL.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows", "SizeToContent"),
            ("PresentationFramework", "System.Windows.Shell", "TaskbarItemInfo"),
            ("PresentationFramework", "System.Windows.Shell", "ThumbButtonInfo"),
            ("PresentationFramework", "System.Windows.Shell", "ThumbButtonInfoCollection"),
            ("PresentationFramework", "System.Windows.Shell", "TaskbarItemProgressState"),
            ("PresentationFramework", "System.Windows.Input", "ICommandSource"),
            ("PresentationFramework", "System.Windows.Controls", "GridSplitter"),
            ("PresentationFramework", "System.Windows.Controls", "GridResizeDirection"),
            ("PresentationFramework", "System.Windows.Controls", "GridResizeBehavior"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(ThumbButtonInfo).IsSubclassOf(typeof(Freezable)));
        Assert.True(typeof(TaskbarItemInfo).IsSubclassOf(typeof(Freezable)));
        Assert.True(typeof(GridSplitter).IsSubclassOf(typeof(Thumb)));
        Assert.True(typeof(ThumbButtonInfoCollection).IsSubclassOf(typeof(System.Collections.ObjectModel.ObservableCollection<ThumbButtonInfo>)));
        Assert.IsAssignableFrom<ICommandSource>(new ThumbButtonInfo());
        Assert.Equal(1, (int)SizeToContent.Width);
        Assert.Equal(3, (int)SizeToContent.WidthAndHeight);
        Assert.Equal(4, (int)TaskbarItemProgressState.Paused);
        Assert.Equal(1, (int)GridResizeDirection.Columns);
        Assert.Equal(1, (int)GridResizeBehavior.CurrentAndNext);
        Assert.Equal(3, (int)GridResizeBehavior.PreviousAndNext);
        Assert.Equal(10.0, new GridSplitter().KeyboardIncrement);
        Assert.Equal(1.0, new GridSplitter().DragIncrement);
    }

    [Fact]
    public void MapCoversEffectsHostsSlice()
    {
        // ScreenToGif effects/host cluster: ShadowPanel/Editor XAML nests
        // DropShadowEffect under Border.Effect/Grid.Effect with literal and
        // {Binding} sub-properties, theme dictionaries keep DropShadowEffect
        // resources, Editor code assigns drawingVisual.Effect, App code sets
        // RenderOptions.ProcessRenderMode, UploadSettings XAML hosts a
        // PasswordBox, FeedbackPreview calls WebBrowser.NavigateToString.
        // The effect/interop names resolve through the default namespace
        // rule onto the same-named engine types; enum values are verified
        // against the GAC PresentationCore/PresentationFramework IL.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media.Effects", "Effect"),
            ("PresentationCore", "System.Windows.Media.Effects", "DropShadowEffect"),
            ("PresentationCore", "System.Windows.Media.Effects", "RenderingBias"),
            ("PresentationCore", "System.Windows.Interop", "RenderMode"),
            ("PresentationCore", "System.Windows.Media", "ContainerVisual"),
            ("PresentationFramework", "System.Windows.Controls", "PasswordBox"),
            ("PresentationFramework", "System.Windows.Controls", "WebBrowser"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(DropShadowEffect).IsSubclassOf(typeof(Effect)));
        Assert.True(typeof(Effect).IsSubclassOf(typeof(Freezable)));
        Assert.True(typeof(ContainerVisual).IsSubclassOf(typeof(Visual)));
        Assert.True(typeof(DrawingVisual).IsSubclassOf(typeof(ContainerVisual)));
        Assert.True(typeof(PasswordBox).IsSubclassOf(typeof(Control)));
        Assert.True(typeof(WebBrowser).IsSubclassOf(typeof(FrameworkElement)));
        Assert.Equal(0, (int)RenderingBias.Performance);
        Assert.Equal(1, (int)RenderingBias.Quality);
        Assert.Equal(0, (int)RenderMode.Default);
        Assert.Equal(1, (int)RenderMode.SoftwareOnly);
        var shadow = new DropShadowEffect();
        Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), shadow.Color);
        Assert.Equal(315, shadow.Direction);
        Assert.Equal(5, shadow.ShadowDepth);
        Assert.Equal(1, shadow.Opacity);
        Assert.Equal(5, shadow.BlurRadius);
        Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
        Assert.Equal('*', new PasswordBox().PasswordChar);
        Assert.Equal(0, new PasswordBox().MaxLength);
        Assert.Equal(RenderMode.Default, RenderOptions.ProcessRenderMode);
    }

    [Fact]
    public void MapCoversMediaPowerSlice()
    {
        // ScreenToGif media/power/basecompat cluster: transition ImageBrush
        // alignment, the ViewModel preview players (MediaPlayer surface the
        // ViewModel subscribes: Opened/Failed/Changed, Open/Pause/Close,
        // Position/Source/Volume/ScrubbingEnabled/NaturalDuration guards),
        // the frame-export DrawVideo render path, recorder suspend guards
        // (PowerModeChanged + Mode == Suspend), and App/Options startup
        // switches (BaseCompatibilityPreferences + nested options).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media", "AlignmentX"),
            ("PresentationCore", "System.Windows.Media", "AlignmentY"),
            ("PresentationCore", "System.Windows.Media", "MediaPlayer"),
            ("PresentationCore", "System.Windows.Media", "ExceptionEventArgs"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModeChangedEventArgs"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModeChangedEventHandler"),
            ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModes"),
            ("WindowsBase", "System.Windows", "BaseCompatibilityPreferences"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.NotNull(WpfTypeMap.MapNestedType("WindowsBase", "System.Windows",
            "BaseCompatibilityPreferences", ["HandleDispatcherRequestProcessingFailureOptions"]));
        Assert.True(typeof(MediaPlayer).IsSubclassOf(typeof(DispatcherObject)));
        Assert.Equal(1, (int)AlignmentX.Center);
        Assert.Equal(1, (int)AlignmentY.Center);
        Assert.Equal(0, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Continue);
        Assert.Equal(1, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Throw);
        Assert.Equal(2, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset);
        Assert.Equal(3, (int)Microsoft.Win32.PowerModes.Suspend);
        var player = new MediaPlayer { Volume = 0, ScrubbingEnabled = true };
        Assert.Equal(0, player.Volume);
        Assert.True(player.ScrubbingEnabled);
        Assert.Null(player.Source);
        Assert.False(player.NaturalDuration.HasTimeSpan);
        Assert.NotNull(typeof(DrawingContext).GetMethod("DrawVideo", [typeof(MediaPlayer), typeof(Rect)]));
        var opened = 0;
        player.MediaOpened += (_, _) => opened++;
        player.Open(new Uri("file:///clip.mp4"));
        Assert.Equal(1, opened);
        Assert.Equal("file:///clip.mp4", player.Source!.AbsoluteUri);
    }

    [Fact]
    public void MapCoversXamlAppLifecycleSlice()
    {
        // ScreenToGif XAML + app-lifecycle cluster: App_Startup/App_Exit
        // handler signatures (Startup/ExitEventArgs + handlers), the
        // Application.Windows live set, Grid definition collections behind
        // RowDefinitions/ColumnDefinitions, ExportPanel's
        // HeaderedContentControl.HeaderProperty resource reference, and the
        // Util localization/clone services (XamlReader/XamlWriter plus the
        // XamlParseException App error reporting catches). PasswordBox mapped
        // by the effects/hosts slice (the canary moves to MediaPlayer).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "StartupEventArgs"),
            ("PresentationFramework", "System.Windows", "StartupEventHandler"),
            ("PresentationFramework", "System.Windows", "ExitEventArgs"),
            ("PresentationFramework", "System.Windows", "ExitEventHandler"),
            ("PresentationFramework", "System.Windows", "WindowCollection"),
            ("PresentationFramework", "System.Windows.Controls", "RowDefinitionCollection"),
            ("PresentationFramework", "System.Windows.Controls", "ColumnDefinitionCollection"),
            ("PresentationFramework", "System.Windows.Controls", "HeaderedContentControl"),
            ("PresentationFramework", "System.Windows.Markup", "XamlReader"),
            ("PresentationFramework", "System.Windows.Markup", "XamlWriter"),
            ("PresentationFramework", "System.Windows.Markup", "XamlParseException"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(HeaderedContentControl).IsSubclassOf(typeof(ContentControl)));
        Assert.True(typeof(RowDefinitionCollection).IsSubclassOf(typeof(System.Collections.ObjectModel.Collection<RowDefinition>)));
        Assert.True(typeof(ColumnDefinitionCollection).IsSubclassOf(typeof(System.Collections.ObjectModel.Collection<ColumnDefinition>)));
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Controls", "PasswordBox"));
    }

    [Fact]
    public void MapCoversPlatformShimsSlice()
    {
        // Slice-30 platform shims: every live ScreenToGif reference into the
        // four framework assemblies absent from upstream build output maps
        // onto an engine counterpart (WinForms window/screen/timer services,
        // mutex ACL retention, AES session protection, the single CodeDom
        // return-slot type). Enum values are verified against live .NET.
        foreach (var (assembly, ns, name) in new[]
        {
            ("System.Windows.Forms", "System.Windows.Forms", "IWin32Window"),
            ("System.Windows.Forms", "System.Windows.Forms", "Keys"),
            ("System.Windows.Forms", "System.Windows.Forms", "Screen"),
            ("System.Windows.Forms", "System.Windows.Forms", "SystemInformation"),
            ("System.Windows.Forms", "System.Windows.Forms", "Timer"),
            ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexRights"),
            ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexAccessRule"),
            ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexSecurity"),
            ("System.Threading.AccessControl", "System.Threading", "ThreadingAclExtensions"),
            ("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography", "DataProtectionScope"),
            ("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography", "ProtectedData"),
            ("System.CodeDom", "System.CodeDom", "CodeTypeDeclaration"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(typeof(System.Windows.Forms.Screen).IsSealed);
        Assert.True(typeof(System.Windows.Forms.Timer).IsSubclassOf(typeof(System.ComponentModel.Component)));
        Assert.True(typeof(System.Windows.Forms.Keys).IsDefined(typeof(FlagsAttribute), inherit: false));
        Assert.Equal(65, (int)System.Windows.Forms.Keys.A);
        Assert.Equal(27, (int)System.Windows.Forms.Keys.Escape);
        Assert.Equal(20, (int)System.Windows.Forms.Keys.CapsLock);
        Assert.Equal(120, System.Windows.Forms.SystemInformation.MouseWheelScrollDelta);
        Assert.Equal(2031617, (int)System.Security.AccessControl.MutexRights.FullControl);
        Assert.Equal(0, (int)System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Assert.Equal(1, (int)System.Security.Cryptography.DataProtectionScope.LocalMachine);
        Assert.True(typeof(System.Security.AccessControl.MutexAccessRule).IsSubclassOf(typeof(System.Security.AccessControl.AccessRule)));
        Assert.Equal("Widget", new System.CodeDom.CodeTypeDeclaration("Widget").Name);
        var blob = System.Security.Cryptography.ProtectedData.Protect(
            new byte[] { 1, 2, 3 }, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Assert.Equal(new byte[] { 1, 2, 3 }, System.Security.Cryptography.ProtectedData.Unprotect(
            blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void MapCoversListBoxItemsSourceSlice()
    {
        // Gallery ListBoxPage (slice-35): the font list binds
        // ItemsSource="{Binding ViewModel.ListBoxItems, Mode=TwoWay}" with
        // SelectedIndex="2". The ListBox type itself already maps onto the
        // engine by the default System.Windows.* namespace rule (so the
        // converted assembly keeps converting with zero gaps); this slice
        // adds the bindable surface on the engine type the XAML binding and
        // converted member references land on.
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Controls", "ListBox"));
        var itemsSource = typeof(ListBox).GetProperty("ItemsSource");
        Assert.NotNull(itemsSource);
        Assert.True(itemsSource.CanWrite && itemsSource.SetMethod?.IsPublic == true);
        Assert.True(itemsSource.PropertyType.IsAssignableFrom(typeof(System.Collections.ObjectModel.ObservableCollection<string>)));
        Assert.NotNull(typeof(ListBox).GetProperty("DisplayMemberPath"));
    }

    [Fact]
    public void NestedFrameworkBaseResolvesAgainstRuntime()
    {
        // KGySoft FlagsEnumConverter's EnumFlagDescriptor derives from the
        // nested framework TypeConverter/SimplePropertyDescriptor, which
        // ships in the framework (System.ComponentModel.TypeConverter is in
        // both the desktop and wasm closures) but is absent from upstream
        // build output. Cecil spells the base with '/' while runtime
        // reflection needs '+'; the converter must bridge that instead of
        // reporting "live but its base cannot resolve".
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("NestedBaseApp", new Version(1, 0, 0, 0)), "NestedBaseApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var converters = new AssemblyNameReference("System.ComponentModel.TypeConverter", new Version(9, 0, 0, 0));
        module.AssemblyReferences.Add(converters);
        var outer = new TypeReference("System.ComponentModel", "TypeConverter", module, converters);
        var nested = new TypeReference(string.Empty, "SimplePropertyDescriptor", module, converters) { DeclaringType = outer };
        Assert.Equal("System.ComponentModel.TypeConverter/SimplePropertyDescriptor", nested.FullName);
        var descriptor = new TypeDefinition("Live", "Descriptor",
            TypeAttributes.Public | TypeAttributes.Class, nested);
        module.Types.Add(descriptor);
        var holder = new TypeDefinition("Live", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        holder.Fields.Add(new FieldDefinition("Active", FieldAttributes.Public, descriptor));
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "NestedBaseApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);

        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-nestedbase"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-nestedbase", "NestedBaseApp.dll"));
        var convertedDescriptor = converted.MainModule.GetType("Live.Descriptor");
        Assert.Equal("System.ComponentModel.TypeConverter/SimplePropertyDescriptor",
            convertedDescriptor.BaseType.FullName);
    }

    [Fact]
    public void RewriteMapsNestedCollectionEnumerators()
    {
        // KGySoft-style foreach lowering over PathFigureCollection names the
        // nested Enumerator in a local plus GetEnumerator/MoveNext/get_Current
        // members: the rewrite must land every position on the engine nested
        // type instead of gaping nested-type.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("NestedApp", new Version(1, 0, 0, 0)), "NestedApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var pc = new AssemblyNameReference("PresentationCore", new Version(4, 0, 0, 0));
        var pf = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        var wb = new AssemblyNameReference("WindowsBase", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(pc);
        module.AssemblyReferences.Add(pf);
        module.AssemblyReferences.Add(wb);
        var figures = new TypeReference("System.Windows.Media", "PathFigureCollection", module, pc);
        var enumerator = new TypeReference(string.Empty, "Enumerator", module, pc) { DeclaringType = figures };
        var holder = new TypeDefinition("NestedApp", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var iterate = new MethodDefinition("Iterate",
            MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        iterate.Body.Variables.Add(new VariableDefinition(enumerator));
        var il = iterate.Body.GetILProcessor();
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Box, enumerator);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        holder.Methods.Add(iterate);
        // Signature positions also carry slice types (options + pen cap).
        var options = new TypeReference("System.Windows", "FrameworkPropertyMetadataOptions", module, pf);
        var cap = new TypeReference("System.Windows.Media", "PenLineCap", module, pc);
        var configure = new MethodDefinition("Configure",
            MethodAttributes.Public | MethodAttributes.Static, cap);
        configure.Parameters.Add(new ParameterDefinition(options));
        configure.Body.GetILProcessor().Emit(OpCodes.Ret);
        holder.Methods.Add(configure);
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "NestedApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);

        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-nested"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-nested", "NestedApp.dll"));
        var convertedIterate = converted.MainModule.GetType("NestedApp.Holder").Methods.First(m => m.Name == "Iterate");
        var local = Assert.Single(convertedIterate.Body.Variables);
        Assert.Equal("DesktopWasm.Wpf", local.VariableType.Scope.Name);
        Assert.True(local.VariableType.IsNested);
        Assert.Equal("Enumerator", local.VariableType.Name);
        Assert.Equal("DesktopWasm.Wpf.PathFigureCollection", local.VariableType.DeclaringType.FullName);
        var convertedConfigure = converted.MainModule.GetType("NestedApp.Holder").Methods.First(m => m.Name == "Configure");
        Assert.Equal("DesktopWasm.Wpf.PenLineCap", convertedConfigure.ReturnType.FullName);
        Assert.Equal("DesktopWasm.Wpf.FrameworkPropertyMetadataOptions", Assert.Single(convertedConfigure.Parameters).ParameterType.FullName);
    }

    [Fact]
    public void ScanFindsRefsPinvokesAndResources()
    {
        using var image = BuildScanFixture();
        var report = AppScanner.Scan(image, "FixtureApp.dll");
        Assert.Contains("PresentationFramework", report.AssemblyReferences);
        Assert.Contains("kernel32.dll", report.PInvokeModules);
        Assert.Contains("Native::Beep", report.PInvokeMethods);
        Assert.Contains("MainWindow.xaml", report.ManifestResources);
        Assert.Contains("references PresentationFramework", report.WpfMarkers);
    }

    [Fact]
    public void RewriteBootsConvertedApp()
    {
        var fixturePath = WriteGeneratedCodeFixture();
        var before = Sha256Of(File.ReadAllBytes(fixturePath));
        var sourceDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "App.xaml"), AppXaml);
        File.WriteAllText(Path.Combine(sourceDir, "MainWindow.xaml"), MainXaml);
        var outDir = Path.Combine(_workDir, "out");

        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(fixturePath, sourceDir, outDir, sourceCommit: "test");

        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        Assert.NotNull(receipt);
        Assert.Equal(WpfAppBoot.MapVersion, receipt.MapVersion);
        Assert.Equal(new[] { "App.xaml", "MainWindow.xaml" }, receipt.EmbeddedXaml);
        // The original is only read, never modified.
        Assert.Equal(before, Sha256Of(File.ReadAllBytes(fixturePath)));

        var converted = File.ReadAllBytes(Path.Combine(outDir, "FixtureApp.dll"));
        var window = WpfAppBoot.Boot(converted);
        Assert.Equal("Demo.MainWindow", window.GetType().FullName);
        Assert.Equal("Converted", window.Title);
        var grid = Assert.IsType<Grid>(window.Content);
        Assert.Equal(2, grid.LogicalChildren.Count);
        var text = Assert.IsType<TextBlock>(grid.LogicalChildren[0]);
        Assert.Equal("Hi", text.Text);
        var fancy = Assert.IsAssignableFrom<DesktopWasm.Wpf.TextBox>(grid.LogicalChildren[1]);
        Assert.Equal("Demo.FancyBox", fancy.GetType().FullName);
        Assert.Equal("F", fancy.Text);
    }

    [Fact]
    public void StripsDeadDesignerBallast()
    {
        var path = WriteBallastFixture(live: false);
        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-ballast"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        Assert.NotNull(receipt);
        Assert.Contains(receipt.Adaptations, a => a.Contains("Dead.Settings"));
        Assert.Contains(receipt.Adaptations, a => a.Contains("MissingLib"));
        using var converted = Mono.Cecil.AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-ballast", "BallastApp.dll"));
        Assert.DoesNotContain(converted.MainModule.Types, t => t.FullName == "Dead.Settings");
        Assert.DoesNotContain(converted.MainModule.AssemblyReferences, r => r.Name == "MissingLib");
    }

    [Fact]
    public void FailsLiveMissingDependency()
    {
        var path = WriteBallastFixture(live: true);
        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-live"));
        Assert.False(ok);
        Assert.Contains(gaps, g => g.Kind == "missing-dependency" && g.Detail.Contains("Dead.Settings"));
    }

    /// <summary>
    /// Designer ballast over a nonexistent library: a dead Settings-like
    /// type, plus (live: true) a field that actually uses it.
    /// </summary>
    private string WriteBallastFixture(bool live)
    {
        var asm = Mono.Cecil.AssemblyDefinition.CreateAssembly(
            new Mono.Cecil.AssemblyNameDefinition("BallastApp", new Version(1, 0, 0, 0)), "BallastApp", Mono.Cecil.ModuleKind.Dll);
        var module = asm.MainModule;
        var missing = new Mono.Cecil.AssemblyNameReference("MissingLib", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(missing);
        var settingsBase = new Mono.Cecil.TypeReference("Missing", "SettingsBase", module, missing);
        var settings = new Mono.Cecil.TypeDefinition("Dead", "Settings",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, settingsBase);
        module.Types.Add(settings);
        var holder = new Mono.Cecil.TypeDefinition("BallastApp", "Holder",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        if (live)
        {
            // Live code names Dead.Settings: stripping would drop behavior.
            var settingsRef = new Mono.Cecil.TypeReference("Dead", "Settings", module, module);
            holder.Fields.Add(new Mono.Cecil.FieldDefinition("Saved",
                Mono.Cecil.FieldAttributes.Public, settingsRef));
        }
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "BallastApp.dll");
        System.IO.Directory.CreateDirectory(_workDir);
        asm.Write(path);
        return path;
    }

    [Fact]
    public void RewriteFailsOnUnknownFrameworkType()
    {
        var fixturePath = WriteUnmappedFixture();
        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            fixturePath, NewEmptySourceDir(), Path.Combine(_workDir, "out2"));
        Assert.False(ok);
        Assert.Contains(gaps, g => g.Detail.Contains("ConnectionStringsSection"));
    }

    [Fact]
    public void RewriteRoutesConfigurationManagerAppSettingsOntoEngine()
    {
        // Directive item P3a: ConfigurationManager itself maps onto the engine
        // scoped adapter, so a get_AppSettings call rewrites to the engine
        // assembly instead of gapping.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("ConfigApp", new Version(1, 0, 0, 0)), "ConfigApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var cfg = new Mono.Cecil.AssemblyNameReference("System.Configuration.ConfigurationManager", new Version(9, 0, 0, 0));
        module.AssemblyReferences.Add(cfg);
        var managerRef = new TypeReference("System.Configuration", "ConfigurationManager", module, cfg);
        var settingsRef = new TypeReference("System.Collections.Specialized", "NameValueCollection", module, module.TypeSystem.Corlib);
        var getAppSettings = new MethodReference("get_AppSettings", settingsRef, managerRef);
        var holder = new TypeDefinition("ConfigApp", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var read = new MethodDefinition("Read",
            MethodAttributes.Public | MethodAttributes.Static, settingsRef);
        var il = read.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, getAppSettings);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        holder.Methods.Add(read);
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "ConfigApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);

        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-config"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-config", "ConfigApp.dll"));
        var convertedCall = converted.MainModule.GetType("ConfigApp.Holder").Methods.First(m => m.Name == "Read");
        var operand = Assert.IsType<MethodReference>(convertedCall.Body.Instructions.First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call).Operand);
        Assert.Equal("ConfigurationManager", operand.DeclaringType.Name);
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)operand.DeclaringType.Scope).Name);
    }

    [Fact]
    public void EmbedAppSettings_PacksSiblingConfigValues()
    {
        // Directive item P3a: the sibling ".dll.config" <appSettings> block
        // travels with the converted image as "__AppSettings.json" so the
        // engine ConfigurationManager adapter serves the real upstream values.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("SettingsApp", new Version(1, 0, 0, 0)), "SettingsApp", ModuleKind.Dll);
        asm.MainModule.Types.Add(new TypeDefinition("SettingsApp", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, asm.MainModule.TypeSystem.Object));
        var path = Path.Combine(_workDir, "SettingsApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);
        File.WriteAllText(path + ".config",
            "<configuration><appSettings>" +
            "<add key=\"GitHub\" value=\"https://example.test/x\" />" +
            "<add key=\"Empty\" value=\"\" />" +
            "</appSettings></configuration>");

        var (ok, gaps, receipt) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-settings"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        Assert.NotNull(receipt);
        Assert.Contains(receipt.Adaptations, a => a.Contains("appSettings", StringComparison.Ordinal) && a.Contains("GitHub", StringComparison.Ordinal));
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-settings", "SettingsApp.dll"));
        var resource = Assert.Single(converted.MainModule.Resources,
            r => r.Name == "__AppSettings.json");
        var json = System.Text.Encoding.UTF8.GetString(
            ((EmbeddedResource)resource).GetResourceData());
        Assert.Contains("GitHub", json, StringComparison.Ordinal);
        Assert.Contains("https://example.test/x", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteFieldReferenceSignatureTypes()
    {
        // ldsfld resolves by name AND field type: rewriting only the
        // declaring type leaves e.g. System.Windows.DependencyProperty
        // behind and the converted image dies at JIT time with
        // MissingFieldException even though the field name is valid.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FieldSigApp", new Version(1, 0, 0, 0)), "FieldSigApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var pf = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        var wb = new AssemblyNameReference("WindowsBase", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(pf);
        module.AssemblyReferences.Add(wb);
        var controlRef = new TypeReference("System.Windows.Controls", "Control", module, pf);
        var dpRef = new TypeReference("System.Windows", "DependencyProperty", module, wb);
        var holder = new TypeDefinition("FieldSigApp", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var read = new MethodDefinition("Read",
            MethodAttributes.Public | MethodAttributes.Static, dpRef);
        var il = read.Body.GetILProcessor();
        il.Emit(OpCodes.Ldsfld, new FieldReference("BackgroundProperty", dpRef, controlRef));
        il.Emit(OpCodes.Ret);
        holder.Methods.Add(read);
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "FieldSigApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);

        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-fieldsig"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-fieldsig", "FieldSigApp.dll"));
        var convertedRead = converted.MainModule.GetType("FieldSigApp.Holder").Methods.First(m => m.Name == "Read");
        var operand = convertedRead.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single();
        Assert.Equal("BackgroundProperty", operand.Name);
        Assert.Equal("DesktopWasm.Wpf.Control", operand.DeclaringType.FullName);
        Assert.Equal(typeof(DesktopWasm.Wpf.Control).Assembly.GetName().Name, operand.DeclaringType.Scope.Name);
        Assert.Equal("DesktopWasm.Wpf.DependencyProperty", operand.FieldType.FullName);
    }

    [Fact]
    public void RetargetFrameworkOnlyMembers()
    {
        // net10 apps may call BCL members the net9 browser runtime lacks
        // (TimeSpan.FromMilliseconds(long) in MainWindowViewModel..ctor):
        // the converter rebinds the call to the widening double overload
        // instead of emitting a MissingMethodException at boot.
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FmApp", new Version(1, 0, 0, 0)), "FmApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var timeSpan = module.ImportReference(typeof(TimeSpan));
        var holder = new TypeDefinition("FmApp", "Holder",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var call = new MethodDefinition("Call",
            MethodAttributes.Public | MethodAttributes.Static, module.ImportReference(typeof(TimeSpan)));
        var target = new MethodReference("FromMilliseconds", module.ImportReference(typeof(TimeSpan)), timeSpan)
        {
            HasThis = false,
        };
        target.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
        var il = call.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I8, 400L);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        holder.Methods.Add(call);
        module.Types.Add(holder);
        var path = Path.Combine(_workDir, "FmApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);

        var (ok, gaps, _) = AssemblyConverter.TryConvert(
            path, NewEmptySourceDir(), Path.Combine(_workDir, "out-fm"));
        Assert.True(ok, $"gaps: {string.Join("; ", gaps.Select(g => $"[{g.Kind}] {g.Detail}"))}");
        using var converted = AssemblyDefinition.ReadAssembly(
            Path.Combine(_workDir, "out-fm", "FmApp.dll"));
        var convertedCall = converted.MainModule.GetType("FmApp.Holder").Methods.First(m => m.Name == "Call");
        var instructions = convertedCall.Body.Instructions.ToList();
        var operand = instructions.Select(i => i.Operand).OfType<MethodReference>().Single();
        var callIndex = instructions.FindIndex(i => ReferenceEquals(i.Operand, operand));
        Assert.True(callIndex > 0, "rebound call needs a preceding instruction");
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Conv_R8, instructions[callIndex - 1].OpCode);
        Assert.Equal("FromMilliseconds", operand.Name);
        Assert.Equal("System.TimeSpan", operand.DeclaringType.FullName);
        var param = Assert.Single(operand.Parameters);
        Assert.Equal("System.Double", param.ParameterType.FullName);
    }

    private static MemoryStream BuildScanFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FixtureApp", new Version(1, 0, 0, 0)), "FixtureApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var fw = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(fw);
        module.Types.Add(new TypeDefinition("FixtureApp", "MainWindow",
            TypeAttributes.Public | TypeAttributes.Class,
            new TypeReference("System.Windows", "Window", module, fw)));
        var native = new TypeDefinition("FixtureApp", "Native",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        var beep = new MethodDefinition("Beep",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl,
            module.TypeSystem.Void);
        var kernel32 = new ModuleReference("kernel32.dll");
        module.ModuleReferences.Add(kernel32);
        beep.PInvokeInfo = new PInvokeInfo(
            PInvokeAttributes.NoMangle | PInvokeAttributes.CallConvWinapi, "Beep", kernel32);
        native.Methods.Add(beep);
        module.Types.Add(native);
        module.Resources.Add(new EmbeddedResource(
            "MainWindow.xaml", ManifestResourceAttributes.Public, new byte[] { (byte)'<' }));
        var stream = new MemoryStream();
        asm.Write(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>Synthesizes PresentationBuildTasks-style generated code.</summary>
    private string WriteGeneratedCodeFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("FixtureApp", new Version(1, 0, 0, 0)), "FixtureApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var fw = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        var xaml = new AssemblyNameReference("System.Xaml", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(fw);
        module.AssemblyReferences.Add(xaml);

        var fakeApp = new TypeReference("System.Windows", "Application", module, fw);
        var fakeWindow = new TypeReference("System.Windows", "Window", module, fw);
        var fakeConnector = new TypeReference("System.Windows.Markup", "IComponentConnector", module, xaml);
        var uriCtor = module.ImportReference(
            typeof(Uri).GetConstructor([typeof(string), typeof(UriKind)])!);
        var loadComponent = new MethodReference("LoadComponent", module.TypeSystem.Void, fakeApp);
        loadComponent.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        loadComponent.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(Uri))));
        var setStartupUri = new MethodReference("set_StartupUri", module.TypeSystem.Void, fakeApp)
        {
            HasThis = true,
        };
        setStartupUri.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(Uri))));

        var app = new TypeDefinition("Demo", "App", TypeAttributes.Public | TypeAttributes.Class, fakeApp);
        var appCtor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        EmitBaseCtor(module, appCtor, fakeApp);
        app.Methods.Add(appCtor);
        var appInit = new MethodDefinition("InitializeComponent",
            MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
        var ai = appInit.Body.GetILProcessor();
        ai.Emit(OpCodes.Ldarg_0);
        ai.Emit(OpCodes.Ldstr, "MainWindow.xaml");
        ai.Emit(OpCodes.Ldc_I4, (int)UriKind.Relative);
        ai.Emit(OpCodes.Newobj, uriCtor);
        ai.Emit(OpCodes.Call, setStartupUri);
        ai.Emit(OpCodes.Ldarg_0);
        ai.Emit(OpCodes.Ldstr, "/FixtureApp;component/app.xaml");
        ai.Emit(OpCodes.Ldc_I4, (int)UriKind.Relative);
        ai.Emit(OpCodes.Newobj, uriCtor);
        ai.Emit(OpCodes.Call, loadComponent);
        ai.Emit(OpCodes.Ret);
        app.Methods.Add(appInit);
        module.Types.Add(app);

        // Custom control over the engine (local: path, like MyTextBox).
        var fancyBase = module.ImportReference(typeof(DesktopWasm.Wpf.TextBox));
        var fancy = new TypeDefinition("Demo", "FancyBox",
            TypeAttributes.Public | TypeAttributes.Class, fancyBase);
        var fancyCtor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        EmitBaseCtor(module, fancyCtor, fancyBase);
        fancy.Methods.Add(fancyCtor);
        module.Types.Add(fancy);

        var window = new TypeDefinition("Demo", "MainWindow",
            TypeAttributes.Public | TypeAttributes.Class, fakeWindow);
        window.Interfaces.Add(new InterfaceImplementation(fakeConnector));
        var winInit = new MethodDefinition("InitializeComponent",
            MethodAttributes.Public | MethodAttributes.Virtual |
            MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        var wi = winInit.Body.GetILProcessor();
        wi.Emit(OpCodes.Ldarg_0);
        wi.Emit(OpCodes.Ldstr, "/FixtureApp;component/mainwindow.xaml");
        wi.Emit(OpCodes.Ldc_I4, (int)UriKind.Relative);
        wi.Emit(OpCodes.Newobj, uriCtor);
        wi.Emit(OpCodes.Call, loadComponent);
        wi.Emit(OpCodes.Ret);
        window.Methods.Add(winInit);
        // Connect dead body: implicit (public) rather than the explicit
        // override real generated code uses — the converter only rewrites
        // references, so either form exercises the same rebind path.
        var connect = new MethodDefinition("Connect",
            MethodAttributes.Public | MethodAttributes.Virtual |
            MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        connect.Parameters.Add(new ParameterDefinition("connectionId", ParameterAttributes.None, module.TypeSystem.Int32));
        connect.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.TypeSystem.Object));
        connect.Body.GetILProcessor().Emit(OpCodes.Ret);
        window.Methods.Add(connect);
        // Generated shape: public MainWindow() { InitializeComponent(); }
        var winCtor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var wc = winCtor.Body.GetILProcessor();
        var winBaseCtor = new MethodReference(".ctor", module.TypeSystem.Void, fakeWindow) { HasThis = true };
        wc.Emit(OpCodes.Ldarg_0);
        wc.Emit(OpCodes.Call, winBaseCtor);
        wc.Emit(OpCodes.Ldarg_0);
        wc.Emit(OpCodes.Call, winInit);
        wc.Emit(OpCodes.Ret);
        window.Methods.Add(winCtor);
        module.Types.Add(window);

        var path = Path.Combine(_workDir, "FixtureApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);
        return path;
    }

    private string WriteUnmappedFixture()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("UnmappedApp", new Version(1, 0, 0, 0)), "UnmappedApp", ModuleKind.Dll);
        var module = asm.MainModule;
        var fw = new AssemblyNameReference("PresentationFramework", new Version(4, 0, 0, 0));
        module.AssemblyReferences.Add(fw);
        // The probe base must stay an unmapped framework type: MediaElement
        // mapped in slice-72, DatePickerTextBox in slice-73, and
        // ConfigurationManager itself in directive item P3a, so the dead
        // derivation now rides System.Configuration.ConnectionStringsSection
        // (no engine counterpart and no browser path, so it never maps).
        var cfg = new Mono.Cecil.AssemblyNameReference("System.Configuration.ConfigurationManager", new Version(9, 0, 0, 0));
        module.AssemblyReferences.Add(cfg);
        module.Types.Add(new TypeDefinition("UnmappedApp", "Scene",
            TypeAttributes.Public | TypeAttributes.Class,
            new TypeReference("System.Configuration", "ConnectionStringsSection", module, cfg)));
        var path = Path.Combine(_workDir, "UnmappedApp.dll");
        Directory.CreateDirectory(_workDir);
        asm.Write(path);
        return path;
    }

    [Fact]
    public void MapCoversCollectionViewSourceSlice()
    {
        // OxyPlot ExampleBrowser boot sequence (verified against the staged
        // ExampleBrowser.WPF assembly): MainWindowViewModel..ctor calls
        // CollectionViewSource.GetDefaultView(ordered examples) and
        // ICollectionView.get_GroupDescriptions plus
        // PropertyGroupDescription..ctor(string). CollectionViewSource rides
        // the default System.Windows.* namespace rule; ICollectionView needs
        // the explicit WindowsBase redirect. MahApps.Metro demo/control and
        // MaterialDesign demo/theme assemblies consume the same two rows.
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Data", "CollectionViewSource"));
        Assert.NotNull(WpfTypeMap.MapType("WindowsBase", "System.ComponentModel", "ICollectionView"));
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Data", "CollectionView"));
        Assert.NotNull(WpfTypeMap.MapType("PresentationFramework", "System.Windows.Data", "ListCollectionView"));
        var viewType = typeof(CollectionViewSource).Assembly.GetType("DesktopWasm.Wpf.ICollectionView");
        Assert.NotNull(viewType);
        Assert.True(viewType.IsInterface);
        Assert.True(viewType.IsAssignableFrom(typeof(CollectionView)));
        Assert.True(viewType.IsAssignableFrom(typeof(ListCollectionView)));
        Assert.NotNull(typeof(CollectionViewSource).GetMethod("GetDefaultView"));
        Assert.NotNull(viewType.GetMethod("get_GroupDescriptions"));
        Assert.NotNull(typeof(PropertyGroupDescription).GetConstructor([typeof(string)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversDispatcherOperationSlice()
    {
        // PC Space Map posts typed dispatcher work (verified member-by-member
        // against the upstream net9.0-windows binary with Mono.Cecil:
        // Dispatcher.BeginInvoke(Delegate, object[]),
        // Dispatcher.BeginInvoke(Delegate, DispatcherPriority, object[]),
        // Dispatcher.InvokeAsync(Func`1) and
        // DispatcherOperation`1.get_Task). The untyped operation already maps;
        // the generic rides the default System.Windows.* namespace rule once
        // the engine declares it (verified against live .NET 9 WindowsBase
        // reflection: sealed, derives DispatcherOperation, new typed
        // Result/Task plus GetAwaiter).
        Assert.NotNull(WpfTypeMap.MapType("WindowsBase", "System.Windows.Threading", "DispatcherOperation"));
        var mapped = WpfTypeMap.MapType("WindowsBase", "System.Windows.Threading", "DispatcherOperation`1");
        Assert.NotNull(mapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DispatcherOperation`1"),
            (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.Windows.Threading", "DispatcherOperation`1")));
        var operationType = typeof(DispatcherOperation<>);
        Assert.Equal("DesktopWasm.Wpf", operationType.Namespace);
        Assert.True(operationType.IsSealed);
        Assert.Equal(typeof(DispatcherOperation), operationType.BaseType);
        Assert.True(typeof(Dispatcher).GetMethods().Any(m => m.Name == "InvokeAsync" && m.IsGenericMethodDefinition));
        Assert.NotNull(typeof(Dispatcher).GetMethod("BeginInvoke",
            [typeof(Delegate), typeof(DispatcherPriority), typeof(object[])]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversMedia3DSlice()
    {
        // HandyControl lib Media3D x5 cluster (slice-78, verified
        // member-by-member against the upstream net10.0-windows
        // HandyControl.dll with Mono.Cecil): all five ride the default
        // System.Windows.* namespace rule once the engine declares them, so
        // no Overrides entry is needed (default-rule map without Overrides,
        // like the DispatcherOperation`1 slice):
        // CoverFlow._point3DAnimation = new Point3DAnimation(Point3D,
        // Duration) through Animatable.BeginAnimation on
        // ProjectionCamera.PositionProperty; CoverFlow keeps only
        // VisualTreeHelper.HitTest(Visual, Point) results that are
        // RayMeshGeometry3DHitTestResult and reads MeshHit;
        // FlipNumber fills ContainerUIElement3D.Children; CoverFlowItem
        // fills Vector3DCollection normals.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media.Media3D", "ContainerUIElement3D"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Vector3DCollection"),
            ("PresentationCore", "System.Windows.Media.Media3D", "ProjectionCamera"),
            ("PresentationCore", "System.Windows.Media.Animation", "Point3DAnimation"),
            ("PresentationCore", "System.Windows.Media.Media3D", "RayMeshGeometry3DHitTestResult"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(ContainerUIElement3D).IsSubclassOf(typeof(UIElement3D)));
        Assert.True(typeof(UIElement3D).IsSubclassOf(typeof(Visual3D)));
        Assert.NotNull(typeof(ContainerUIElement3D).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(ContainerUIElement3D).GetProperty("Children"));
        Assert.NotNull(typeof(Vector3DCollection).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(Vector3DCollection).GetMethod("Add", [typeof(Vector3D)]));
        Assert.True(typeof(ProjectionCamera).IsSubclassOf(typeof(Camera)));
        Assert.True(typeof(PerspectiveCamera).IsSubclassOf(typeof(ProjectionCamera)));
        Assert.NotNull(typeof(ProjectionCamera).GetField("PositionProperty"));
        Assert.NotNull(typeof(ProjectionCamera).GetProperty("Position"));
        Assert.NotNull(typeof(Point3DAnimation).GetConstructor([typeof(Point3D), typeof(Duration)]));
        Assert.NotNull(typeof(RayMeshGeometry3DHitTestResult).GetProperty("MeshHit"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversViewPrereqsSlice()
    {
        // OxyPlot view prerequisites (slice-48, verified member-by-member
        // against the staged net8.0-windows OxyPlot.Wpf/OxyPlot.Wpf.Shared
        // binaries plus live .NET 10 reflection): the 8 unmapped view types
        // ride the default System.Windows.* namespace rule, while the 4
        // absent document/print/settings assemblies need explicit redirects
        // onto the PlatformShims.cs engine counterparts.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Documents.Serialization", "SerializerWriter"),
            ("PresentationFramework", "System.Windows.Media", "TextOptions"),
            ("PresentationCore", "System.Windows.Media", "ClearTypeHint"),
            ("PresentationCore", "System.Windows.Input", "ManipulationStartedEventArgs"),
            ("PresentationCore", "System.Windows.Input", "ManipulationDeltaEventArgs"),
            ("PresentationCore", "System.Windows.Input", "ManipulationDelta"),
            ("PresentationCore", "System.Windows.Input", "ManipulationCompletedEventArgs"),
            ("PresentationFramework", "System.Windows.Data", "ValueConversionAttribute"),
            ("System.IO.Packaging", "System.IO.Packaging", "Package"),
            ("ReachFramework", "System.Windows.Xps.Packaging", "XpsDocument"),
            ("System.Printing", "System.Windows.Xps", "XpsDocumentWriter"),
            ("System.Printing", "System.Printing", "PrintQueue"),
            ("System.Printing", "System.Printing", "PrintDocumentImageableArea"),
            ("System.Configuration.ConfigurationManager", "System.Configuration", "SettingsBase"),
            ("System.Configuration.ConfigurationManager", "System.Configuration", "ApplicationSettingsBase"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.Equal(0, (int)ClearTypeHint.Auto);
        Assert.Equal(1, (int)ClearTypeHint.Enabled);
        Assert.NotNull(typeof(TextOptions).GetMethod("SetTextFormattingMode",
            [typeof(DependencyObject), typeof(TextFormattingMode)]));
        Assert.NotNull(typeof(TextOptions).GetMethod("GetTextFormattingMode",
            [typeof(DependencyObject)]));
        Assert.NotNull(typeof(RenderOptions).GetField("ClearTypeHintProperty"));
        Assert.NotNull(typeof(UIElement).GetMethod("OnManipulationStarted",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(UIElement).GetMethod("OnManipulationDelta",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(UIElement).GetMethod("OnManipulationCompleted",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(ValueConversionAttribute).GetConstructor([typeof(Type), typeof(Type)]));
        Assert.True(typeof(System.IO.Packaging.Package).IsAbstract);
        Assert.True(typeof(System.Windows.Xps.XpsDocumentWriter).IsSubclassOf(typeof(SerializerWriter)));
        Assert.NotNull(typeof(System.Windows.Xps.Packaging.XpsDocument).GetConstructor([typeof(System.IO.Packaging.Package)]));
        Assert.NotNull(typeof(System.Configuration.SettingsBase).GetMethod("Synchronized",
            [typeof(System.Configuration.SettingsBase)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversAvalonDockSlice()
    {
        // AvalonDock prerequisites (slice-60, verified member-by-member
        // against the upstream net9.0-windows AvalonDock.dll with Mono.Cecil
        // plus live .NET 9 PresentationFramework/PresentationCore reflection:
        // ComponentResourceKey.(Type, object) only; GeneratorStatus as the
        // ItemContainerGenerator.get_Status/add_remove_StatusChanged contract;
        // HwndHost .ctor/Dispose/UpdateWindowPos/MeasureOverride override/
        // get_Handle on the LayoutAutoHideWindowControl subclass;
        // ToolBar.get_ButtonStyleKey only; KeyBinding.(ICommand, KeyGesture)
        // with UIElement.get_InputBindings plus InputBindingCollection.Add/
        // Remove). All seven slice types ride the default System.Windows.*
        // namespace rule once the engine declares them (ComponentResourceKey
        // alone covers 5 rows: AvalonDock plus 4 theme assemblies; verified:
        // enum numerics GeneratorStatus 0-3 and OverflowMode 0-2, abstract
        // BuildWindowCore/DestroyWindowCore, read-only Orientation,
        // attached IsOverflowItem/OverflowMode).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "ComponentResourceKey"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "GeneratorStatus"),
            ("PresentationFramework", "System.Windows.Interop", "HwndHost"),
            ("PresentationFramework", "System.Windows.Controls", "ToolBar"),
            ("PresentationFramework", "System.Windows.Controls", "OverflowMode"),
            ("PresentationCore", "System.Windows.Input", "InputBinding"),
            ("PresentationCore", "System.Windows.Input", "KeyBinding"),
            ("PresentationCore", "System.Windows.Input", "InputBindingCollection"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        // The generic-dispatch and collection-view rules from slices 40-56
        // are untouched: no explicit Overrides for this slice.
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows", "ComponentResourceKey")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows.Interop", "HwndHost")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "KeyBinding")));
        Assert.Equal(0, (int)GeneratorStatus.NotStarted);
        Assert.Equal(3, (int)GeneratorStatus.Error);
        Assert.Equal(0, (int)OverflowMode.AsNeeded);
        Assert.Equal(2, (int)OverflowMode.Never);
        Assert.True(typeof(HwndHost).IsAbstract);
        Assert.True(typeof(InputBinding).IsSubclassOf(typeof(Freezable)));
        Assert.True(typeof(KeyBinding).IsSubclassOf(typeof(InputBinding)));
        Assert.True(typeof(InputBindingCollection).IsSealed);
        Assert.NotNull(typeof(ComponentResourceKey).GetConstructor([typeof(Type), typeof(object)]));
        Assert.NotNull(typeof(ToolBar).GetMethod("get_ButtonStyleKey"));
        Assert.NotNull(typeof(ItemContainerGenerator).GetProperty("Status"));
        Assert.NotNull(typeof(ItemContainerGenerator).GetEvent("StatusChanged"));
        Assert.NotNull(typeof(UIElement).GetProperty("InputBindings"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversMouseBindingSlice()
    {
        // HandyControl mouse scope (slice-74, verified member-by-member
        // against the upstream net8.0-windows HandyControl.dll with
        // Mono.Cecil: ImageViewer.OnMoveGestureChanged calls
        // MouseBinding..ctor(ICommand, MouseGesture); ImageViewer.cctor,
        // ToggleBlock.cctor and SimpleMouseBinding call
        // MouseGesture..ctor(MouseAction); ImageViewer/ToggleBlock read
        // get_MouseAction; SimpleMouseBinding syncs set_MouseAction).
        // Shapes live-verified against the 9.0.21 PresentationCore ref pack
        // (MouseAction 0-7, public ()/(ICommand, MouseGesture) ctors,
        // MouseActionProperty DP, non-sealed classes, no gesture DPs).
        // All three ride the default System.Windows.* rule: no Overrides.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Input", "MouseBinding"),
            ("PresentationCore", "System.Windows.Input", "MouseGesture"),
            ("PresentationCore", "System.Windows.Input", "MouseAction"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "MouseBinding")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "MouseGesture")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "MouseAction")));
        Assert.Equal(0, (int)MouseAction.None);
        Assert.Equal(1, (int)MouseAction.LeftClick);
        Assert.Equal(4, (int)MouseAction.WheelClick);
        Assert.Equal(7, (int)MouseAction.MiddleDoubleClick);
        Assert.True(typeof(MouseBinding).IsSubclassOf(typeof(InputBinding)));
        Assert.False(typeof(MouseBinding).IsSealed);
        Assert.False(typeof(MouseGesture).IsSealed);
        Assert.NotNull(typeof(MouseBinding).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(MouseBinding).GetConstructor([typeof(ICommand), typeof(MouseGesture)]));
        Assert.NotNull(typeof(MouseGesture).GetConstructor([typeof(MouseAction)]));
        Assert.NotNull(typeof(MouseGesture).GetConstructor([typeof(MouseAction), typeof(ModifierKeys)]));
        Assert.NotNull(typeof(MouseBinding).GetField("MouseActionProperty"));
        Assert.NotNull(typeof(MouseBinding).GetProperty("MouseAction"));
        Assert.NotNull(typeof(MouseGesture).GetProperty("MouseAction"));
        Assert.NotNull(typeof(MouseGesture).GetProperty("Modifiers"));
        Assert.NotNull(typeof(MouseGesture).GetMethod("Matches"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversTouchSlice()
    {
        // HandyControl entry touch scope (slice-75, verified member-by-member
        // against the upstream net10.0-windows HandyControlDemo.dll with
        // Mono.Cecil: TouchDragMoveWindow.ctor subscribes
        // UIElement.add_TouchDown/add_TouchUp(EventHandler<TouchEventArgs>),
        // MainWindow_TouchDown reads TouchEventArgs.get_TouchDevice and calls
        // UIElement.CaptureTouch(TouchDevice); the lib itself carries no
        // touch references). Shapes live-verified against the 9.0.20
        // PresentationCore ref pack (TouchAction Down=0/Move=1/Up=2,
        // TouchEventArgs non-sealed with a public (TouchDevice, int) ctor,
        // TouchDevice abstract with a protected (int) ctor,
        // TouchPoint.(TouchDevice, Point, Rect, TouchAction) with
        // Action/Bounds/Position/Size/TouchDevice, TouchPointCollection as a
        // Collection-of-TouchPoint). Both slice types ride the default
        // System.Windows.* rule: no Overrides.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Input", "TouchEventArgs"),
            ("PresentationCore", "System.Windows.Input", "TouchDevice"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "TouchEventArgs")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", "TouchDevice")));
        Assert.Equal(0, (int)TouchAction.Down);
        Assert.Equal(1, (int)TouchAction.Move);
        Assert.Equal(2, (int)TouchAction.Up);
        Assert.False(typeof(TouchEventArgs).IsSealed);
        Assert.True(typeof(TouchDevice).IsAbstract);
        Assert.NotNull(typeof(TouchEventArgs).GetConstructor([typeof(TouchDevice), typeof(int)]));
        Assert.NotNull(typeof(TouchEventArgs).GetProperty("TouchDevice"));
        Assert.NotNull(typeof(TouchEventArgs).GetMethod("GetTouchPoint"));
        Assert.NotNull(typeof(TouchEventArgs).GetMethod("GetIntermediateTouchPoints"));
        Assert.NotNull(typeof(TouchDevice).GetProperty("Id"));
        Assert.NotNull(typeof(TouchDevice).GetProperty("Captured"));
        Assert.NotNull(typeof(TouchDevice).GetProperty("DirectlyOver"));
        Assert.NotNull(typeof(TouchDevice).GetMethod("Capture", [typeof(IInputElement)]));
        Assert.NotNull(typeof(TouchDevice).GetMethod("Capture", [typeof(IInputElement), typeof(CaptureMode)]));
        Assert.NotNull(typeof(TouchDevice).GetMethod("GetTouchPoint"));
        Assert.NotNull(typeof(TouchPoint).GetConstructor(
            [typeof(TouchDevice), typeof(Point), typeof(Rect), typeof(TouchAction)]));
        Assert.NotNull(typeof(UIElement).GetMethod("add_TouchDown"));
        Assert.NotNull(typeof(UIElement).GetMethod("remove_TouchDown"));
        Assert.NotNull(typeof(UIElement).GetMethod("add_TouchUp"));
        Assert.NotNull(typeof(UIElement).GetMethod("remove_TouchUp"));
        Assert.NotNull(typeof(UIElement).GetMethod("CaptureTouch"));
        Assert.NotNull(typeof(UIElement).GetMethod("ReleaseTouchCapture"));
        Assert.NotNull(typeof(UIElement).GetMethod("ReleaseAllTouchCaptures"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversSlice76Entry()
    {
        // HandyControl entry splash/audio scope (slice-76, verified
        // member-by-member against the upstream net10.0-windows
        // HandyControlDemo.dll with Mono.Cecil: App.OpenSplashScreen calls
        // SplashScreen..ctor(string)/Show(bool); ChatBoxViewModel.ReadMessage
        // calls SoundPlayer..ctor(string)/PlaySync, the only
        // System.Windows.Extensions reference in the entry). Shapes
        // live-verified against the 9.0.21 pack (WindowsBase SplashScreen:
        // .ctor(Assembly, string)/.ctor(string), Close(TimeSpan),
        // Show(bool)/Show(bool, bool), object base; System.Windows.Extensions
        // SoundPlayer: Component base, ()/(Stream)/(string) ctors,
        // IsLoadCompleted/LoadTimeout/SoundLocation/Stream/Tag,
        // LoadCompleted/SoundLocationChanged/StreamChanged,
        // Load/LoadAsync/On*/Play/PlayLooping/PlaySync/Stop).
        // SplashScreen rides the default System.Windows.* rule (no
        // Overrides); SoundPlayer needs one (System.Media is outside it).
        Assert.NotNull(WpfTypeMap.MapType("WindowsBase", "System.Windows", "SplashScreen"));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.Windows", "SplashScreen")));
        var sound = WpfTypeMap.MapType("System.Windows.Extensions", "System.Media", "SoundPlayer");
        Assert.NotNull(sound);
        Assert.Equal(("DesktopWasm.Wpf", "System.Media", "SoundPlayer"), sound!.Value);
        Assert.True(WpfTypeMap.Overrides.ContainsKey(("System.Windows.Extensions", "System.Media", "SoundPlayer")));
        Assert.False(typeof(SplashScreen).IsSealed);
        Assert.Equal(typeof(object), typeof(SplashScreen).BaseType);
        Assert.NotNull(typeof(SplashScreen).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(SplashScreen).GetConstructor([typeof(System.Reflection.Assembly), typeof(string)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Show", [typeof(bool)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Show", [typeof(bool), typeof(bool)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Close", [typeof(TimeSpan)]));
        Assert.True(typeof(System.Media.SoundPlayer).IsSubclassOf(typeof(System.ComponentModel.Component)));
        Assert.NotNull(typeof(System.Media.SoundPlayer).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(System.Media.SoundPlayer).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(System.Media.SoundPlayer).GetConstructor([typeof(Stream)]));
        Assert.NotNull(typeof(System.Media.SoundPlayer).GetMethod("PlaySync", Type.EmptyTypes));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversAvalonDockSlice61()
    {
        // Slice-61 remainder (member evidence via Mono.Cecil on the upstream
        // net48 TestApp-closure AvalonDock.dll plus the VS theme, shapes
        // verified live against the 9.0.21 pack): every row rides the default
        // System.Windows.* namespace rule once the engine declares it, so no
        // explicit Overrides are added.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "DependencyPropertyHelper"),
            ("PresentationFramework", "System.Windows", "ValueSource"),
            ("PresentationFramework", "System.Windows", "BaseValueSource"),
            ("PresentationFramework", "System.Windows", "GridLengthConverter"),
            ("PresentationFramework", "System.Windows", "FrameworkElementFactory"),
            ("PresentationFramework", "System.Windows.Controls", "StyleSelector"),
            ("PresentationCore", "System.Windows.Media.Imaging", "IconBitmapDecoder"),
            ("WindowsBase", "System.Windows.Threading", "DispatcherOperationCallback"),
            ("System.Xaml", "System.Windows.Markup", "MarkupExtensionReturnTypeAttribute"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows", "DependencyPropertyHelper")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows", "FrameworkElementFactory")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media.Imaging", "IconBitmapDecoder")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.Windows.Threading", "DispatcherOperationCallback")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("System.Xaml", "System.Windows.Markup", "MarkupExtensionReturnTypeAttribute")));
        Assert.Equal(0, (int)BaseValueSource.Unknown);
        Assert.Equal(11, (int)BaseValueSource.Local);
        Assert.NotNull(typeof(DependencyPropertyHelper).GetMethod("GetValueSource",
            [typeof(DependencyObject), typeof(DependencyProperty)]));
        Assert.NotNull(typeof(DependencyPropertyHelper).GetMethod("IsTemplatedValueDynamic",
            [typeof(DependencyObject), typeof(DependencyProperty)]));
        Assert.NotNull(typeof(ValueSource).GetProperty("BaseValueSource"));
        Assert.NotNull(typeof(ValueSource).GetProperty("IsExpression"));
        Assert.NotNull(typeof(ValueSource).GetProperty("IsAnimated"));
        Assert.NotNull(typeof(ValueSource).GetProperty("IsCoerced"));
        Assert.NotNull(typeof(ValueSource).GetProperty("IsCurrent"));
        Assert.True(typeof(GridLengthConverter).IsSubclassOf(typeof(System.ComponentModel.TypeConverter)));
        Assert.Equal(typeof(FrameworkElementFactory),
            typeof(FrameworkTemplate).GetProperty("VisualTree")!.PropertyType);
        Assert.Equal(typeof(UIElement),
            typeof(ControlTemplate).GetProperty("VisualTree", typeof(UIElement))!.PropertyType);
        Assert.NotNull(typeof(FrameworkElementFactory).GetConstructor([typeof(Type), typeof(string)]));
        Assert.NotNull(typeof(FrameworkElementFactory).GetMethod("AppendChild"));
        Assert.NotNull(typeof(FrameworkElementFactory).GetMethod("SetBinding"));
        Assert.NotNull(typeof(FrameworkElementFactory).GetMethod("SetValue", [typeof(DependencyProperty), typeof(object)]));
        Assert.NotNull(typeof(StyleSelector).GetMethod("SelectStyle"));
        Assert.True(typeof(IconBitmapDecoder).IsSubclassOf(typeof(BitmapDecoder)));
        Assert.True(typeof(DispatcherOperationCallback).IsSubclassOf(typeof(MulticastDelegate)));
        Assert.NotNull(typeof(MarkupExtensionReturnTypeAttribute).GetConstructor([typeof(Type)]));
        Assert.NotNull(typeof(DependencyProperty).GetProperty("ReadOnly"));
        Assert.NotNull(typeof(DependencyProperty).GetMethod("GetMetadata", [typeof(Type)]));
        Assert.NotNull(typeof(DependencyProperty).GetMethod("GetMetadata", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(FrameworkElement).GetMethod("SetBinding",
            [typeof(DependencyProperty), typeof(BindingBase)]));
        Assert.NotNull(typeof(ControlTemplate).GetConstructor([typeof(Type)]));
        Assert.Equal(AttributeTargets.Class,
            ((AttributeUsageAttribute)Attribute.GetCustomAttribute(
                typeof(MarkupExtensionReturnTypeAttribute), typeof(AttributeUsageAttribute))!).ValidOn);
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversMaterialEntrySlice()
    {
        // MaterialDesignDemo entry prerequisites (slice-69, verified
        // member-by-member against the upstream net8.0-windows
        // MaterialDesignDemo.dll/MaterialDesignDemo.Shared.dll/
        // MaterialDesignThemes.Wpf.dll binaries with Mono.Cecil plus live
        // .NET 9 PresentationFramework/PresentationCore reflection): the
        // demo reads Fonts.get_SystemFontFamilies plus XmlLanguage
        // GetLanguage/GetSpecificCulture, themes add/remove ScrollBar.Scroll
        // plus set_ViewportSize and drive the CustomPopupPlacement callback
        // shape, and the entry constructs ExceptionValidationRule. Every row
        // rides the default System.Windows.* namespace rule once the engine
        // declares it, so no explicit Overrides are added.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media", "Fonts"),
            ("System.Xaml", "System.Windows.Markup", "XmlLanguage"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ScrollBar"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "Track"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ScrollEventArgs"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ScrollEventHandler"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ScrollEventType"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "CustomPopupPlacement"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "CustomPopupPlacementCallback"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "PopupPrimaryAxis"),
            ("PresentationFramework", "System.Windows.Controls", "ExceptionValidationRule"),
            ("System.Windows.Presentation", "System.Windows.Threading", "DispatcherExtensions"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media", "Fonts")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("System.Xaml", "System.Windows.Markup", "XmlLanguage")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows.Controls.Primitives", "ScrollBar")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows.Controls", "ExceptionValidationRule")));
        Assert.Equal(0, (int)PopupPrimaryAxis.None);
        Assert.Equal(1, (int)PopupPrimaryAxis.Horizontal);
        Assert.Equal(2, (int)PopupPrimaryAxis.Vertical);
        Assert.Equal(0, (int)ScrollEventType.EndScroll);
        Assert.Equal(8, (int)ScrollEventType.ThumbTrack);
        Assert.Equal(Orientation.Vertical, new ScrollBar().Orientation);
        Assert.True(typeof(ScrollBar).IsSubclassOf(typeof(RangeBase)));
        Assert.True(typeof(ExceptionValidationRule).IsSubclassOf(typeof(ValidationRule)));
        Assert.True(typeof(ExceptionValidationRule).IsSealed);
        Assert.True(typeof(CustomPopupPlacementCallback).IsSubclassOf(typeof(MulticastDelegate)));
        Assert.NotNull(typeof(Fonts).GetMethod("GetFontFamilies", [typeof(string)]));
        Assert.NotNull(typeof(XmlLanguage).GetMethod("GetLanguage", [typeof(string)]));
        Assert.NotNull(typeof(ScrollBar).GetEvent("Scroll"));
        Assert.NotNull(typeof(Track).GetMethod("ValueFromPoint", [typeof(Point)]));
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DispatcherExtensions"),
            WpfTypeMap.MapType("System.Windows.Presentation", "System.Windows.Threading", "DispatcherExtensions"));
        Assert.NotNull(typeof(DispatcherExtensions).GetMethod("Invoke", [typeof(Dispatcher), typeof(Action)]));
        Assert.NotNull(typeof(DispatcherExtensions).GetMethod("BeginInvoke", [typeof(Dispatcher), typeof(Action)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversThemeClosureSlice()
    {
        // MaterialDesign theme closure (slice-70, member-evidenced with
        // Mono.Cecil against the upstream net8.0-windows
        // MaterialDesignThemes.Wpf.dll plus live 9.0.21 ref-pack and runtime
        // probes): automation peers, TextSelection, GridView family,
        // animation keyframes/groups/easings, NameScope/INameScope,
        // BlurEffect and SkewTransform. Every row rides the default
        // System.Windows.* namespace rule once the engine declares it
        // (INameScope lives under System.Windows.Markup, which the rule
        // covers), so no explicit Overrides are added.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Automation.Peers", "ListBoxItemAutomationPeer"),
            ("PresentationFramework", "System.Windows.Automation.Peers", "SelectorAutomationPeer"),
            ("PresentationFramework", "System.Windows.Automation.Peers", "ItemAutomationPeer"),
            ("PresentationFramework", "System.Windows.Automation.Peers", "ItemsControlAutomationPeer"),
            ("PresentationFramework", "System.Windows.Automation.Peers", "ListViewAutomationPeer"),
            ("PresentationCore", "System.Windows.Automation.Peers", "PatternInterface"),
            ("PresentationCore", "System.Windows.Automation.Peers", "AutomationControlType"),
            ("PresentationFramework", "System.Windows.Documents", "TextSelection"),
            ("PresentationFramework", "System.Windows.Documents", "TextRange"),
            ("PresentationFramework", "System.Windows.Documents", "TextPointer"),
            ("PresentationFramework", "System.Windows.Controls", "GridView"),
            ("PresentationFramework", "System.Windows.Controls", "GridViewColumn"),
            ("PresentationFramework", "System.Windows.Controls", "GridViewColumnCollection"),
            ("PresentationFramework", "System.Windows.Controls", "ViewBase"),
            ("PresentationCore", "System.Windows.Media.Animation", "Animatable"),
            ("PresentationCore", "System.Windows.Media.Animation", "TimelineGroup"),
            ("PresentationCore", "System.Windows.Media.Animation", "ParallelTimeline"),
            ("PresentationCore", "System.Windows.Media.Animation", "DoubleAnimationUsingKeyFrames"),
            ("PresentationCore", "System.Windows.Media.Animation", "DoubleKeyFrameCollection"),
            ("PresentationCore", "System.Windows.Media.Animation", "DoubleKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "DiscreteDoubleKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "LinearDoubleKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "EasingDoubleKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "SineEase"),
            ("PresentationCore", "System.Windows.Media.Animation", "CubicEase"),
            ("WindowsBase", "System.Windows", "NameScope"),
            ("System.Xaml", "System.Windows.Markup", "INameScope"),
            ("PresentationCore", "System.Windows.Media.Effects", "BlurEffect"),
            ("PresentationCore", "System.Windows.Media.Effects", "KernelType"),
            ("PresentationCore", "System.Windows.Media", "SkewTransform"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("System.Xaml", "System.Windows.Markup", "INameScope")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media.Effects", "BlurEffect")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media.Animation", "DoubleAnimationUsingKeyFrames")));
        // Converted call shapes bind against the engine (member-level pins):
        // Animatable hosts, Timeline.Completed, Storyboard seek/resume and
        // DependencyObject-typed statics, key-frame ctors, peer overrides.
        Assert.NotNull(typeof(Animatable).GetMethod("BeginAnimation", [typeof(DependencyProperty), typeof(AnimationTimeline)]));
        Assert.NotNull(typeof(Timeline).GetEvent("Completed"));
        Assert.NotNull(typeof(Storyboard).GetMethod("Seek", [typeof(TimeSpan)]));
        Assert.NotNull(typeof(Storyboard).GetMethod("Resume", Type.EmptyTypes));
        Assert.NotNull(typeof(Storyboard).GetMethod("SetTarget", [typeof(DependencyObject), typeof(DependencyObject)]));
        Assert.NotNull(typeof(Storyboard).GetMethod("SetTargetProperty", [typeof(DependencyObject), typeof(PropertyPath)]));
        Assert.NotNull(typeof(DoubleKeyFrame).GetProperty("KeyTime"));
        Assert.NotNull(typeof(DoubleAnimationUsingKeyFrames).GetProperty("KeyFrames"));
        Assert.NotNull(typeof(ItemsControlAutomationPeer).GetMethod("CreateItemAutomationPeer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(AutomationPeer).GetMethod("GetPattern", [typeof(PatternInterface)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversVisualStateSlice()
    {
        // MaterialDesign visual-state cluster (slice-71, member-evidenced with
        // Mono.Cecil against the upstream net8.0-windows MaterialDesignThemes.Wpf.dll
        // and Microsoft.Xaml.Behaviors.dll plus live 9.0.21 ref-pack and runtime
        // probes): VisualStateManager/VisualState/VisualStateGroup/VisualTransition
        // with the FreezableCollection<T> concrete lists plus
        // TemplateVisualStateAttribute. Every row rides the default
        // System.Windows.* namespace rule once the engine declares it, so no
        // explicit Overrides are added.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows", "VisualStateManager"),
            ("PresentationFramework", "System.Windows", "VisualState"),
            ("PresentationFramework", "System.Windows", "VisualStateGroup"),
            ("PresentationFramework", "System.Windows", "VisualStateChangedEventArgs"),
            ("PresentationFramework", "System.Windows", "VisualTransition"),
            ("PresentationFramework", "System.Windows", "TemplateVisualStateAttribute"),
            ("PresentationCore", "System.Windows", "FreezableCollection`1"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows", "VisualStateManager")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows", "FreezableCollection`1")));
        // Converted call shapes bind against the engine (member-level pins):
        // GoToState/GoToElementState/GetVisualStateGroups/Custom manager,
        // the protected GoToStateCore override slot Behaviors derives from,
        // state/group/transition accessors, and the generic list ctors.
        Assert.NotNull(typeof(VisualStateManager).GetMethod("GoToState", [typeof(FrameworkElement), typeof(string), typeof(bool)]));
        Assert.NotNull(typeof(VisualStateManager).GetMethod("GoToElementState", [typeof(FrameworkElement), typeof(string), typeof(bool)]));
        Assert.NotNull(typeof(VisualStateManager).GetMethod("GetVisualStateGroups", [typeof(FrameworkElement)]));
        Assert.NotNull(typeof(VisualStateManager).GetMethod("GetCustomVisualStateManager", [typeof(FrameworkElement)]));
        Assert.NotNull(typeof(VisualStateManager).GetMethod("SetCustomVisualStateManager", [typeof(FrameworkElement), typeof(VisualStateManager)]));
        Assert.NotNull(typeof(VisualStateManager).GetMethod("GoToStateCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(VisualState).GetProperty("Name"));
        Assert.NotNull(typeof(VisualState).GetProperty("Storyboard"));
        Assert.NotNull(typeof(VisualStateGroup).GetProperty("States"));
        Assert.NotNull(typeof(VisualStateGroup).GetProperty("Transitions"));
        Assert.NotNull(typeof(VisualStateGroup).GetProperty("CurrentState"));
        Assert.NotNull(typeof(VisualStateGroup).GetEvent("CurrentStateChanging"));
        Assert.NotNull(typeof(VisualStateGroup).GetEvent("CurrentStateChanged"));
        Assert.NotNull(typeof(VisualTransition).GetProperty("From"));
        Assert.NotNull(typeof(VisualTransition).GetProperty("To"));
        Assert.NotNull(typeof(VisualTransition).GetProperty("GeneratedDuration"));
        Assert.NotNull(typeof(VisualTransition).GetProperty("GeneratedEasingFunction"));
        Assert.NotNull(typeof(VisualTransition).GetProperty("Storyboard"));
        Assert.NotNull(typeof(TemplateVisualStateAttribute).GetProperty("Name"));
        Assert.NotNull(typeof(TemplateVisualStateAttribute).GetProperty("GroupName"));
        Assert.NotNull(typeof(FreezableCollection<VisualState>).GetConstructor([typeof(IEnumerable<VisualState>)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversSlice72()
    {
        // Slice-72 remainder (member-evidenced with Mono.Cecil against the
        // upstream net8.0-windows MaterialDesignThemes.Wpf.dll and
        // Microsoft.Xaml.Behaviors.dll plus live 9.0.20 ref-pack dumps):
        // the weak-event/items/request route, the XAML parser pair, the
        // spell pair, the DataGrid column cluster, the Media3D cluster, the
        // media element, the key-frame families, the manipulation cluster,
        // the shader contract and the clock enum. Every System.Windows.* row
        // rides the default namespace rule once the engine declares it; only
        // System.Collections.Specialized CollectionChangedEventManager needs
        // an explicit Override.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "SpellCheck"),
            ("PresentationFramework", "System.Windows.Controls", "SpellingError"),
            ("PresentationFramework", "System.Windows.Markup", "ParserContext"),
            ("PresentationFramework", "System.Windows.Markup", "XmlnsDictionary"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ItemsChangedEventArgs"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "ItemsChangedEventHandler"),
            ("PresentationFramework", "System.Windows", "RequestBringIntoViewEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridComboBoxColumn"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridAutoGeneratingColumnEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridCheckBoxColumn"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridTemplateColumn"),
            ("PresentationFramework", "System.Windows.Controls", "MediaElement"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "LayoutInformation"),
            ("PresentationCore", "System.Windows.Media.Animation", "ThicknessAnimationUsingKeyFrames"),
            ("PresentationCore", "System.Windows.Media.Animation", "ThicknessKeyFrameCollection"),
            ("PresentationCore", "System.Windows.Media.Animation", "ThicknessKeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "Int32AnimationUsingKeyFrames"),
            ("PresentationCore", "System.Windows.Media.Animation", "Int32KeyFrameCollection"),
            ("PresentationCore", "System.Windows.Media.Animation", "Int32KeyFrame"),
            ("PresentationCore", "System.Windows.Media.Animation", "Int32Animation"),
            ("PresentationCore", "System.Windows.Media.Animation", "PointAnimation"),
            ("PresentationCore", "System.Windows.Media.Animation", "ClockState"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Quaternion"),
            ("PresentationCore", "System.Windows.Media.Media3D", "QuaternionRotation3D"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Viewport2DVisual3D"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Transform3DCollection"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Model3DGroup"),
            ("PresentationCore", "System.Windows.Media.Media3D", "Model3DCollection"),
            ("PresentationCore", "System.Windows.Input", "CaptureMode"),
            ("PresentationCore", "System.Windows.Input", "ManipulationModes"),
            ("PresentationCore", "System.Windows.Input", "ManipulationStartingEventArgs"),
            ("PresentationCore", "System.Windows.Input", "ManipulationInertiaStartingEventArgs"),
            ("PresentationCore", "System.Windows.Input", "ManipulationVelocities"),
            ("PresentationCore", "System.Windows.Input", "InertiaTranslationBehavior"),
            ("PresentationCore", "System.Windows.Input", "InertiaRotationBehavior"),
            ("PresentationCore", "System.Windows.Media.Effects", "ShaderEffect"),
            ("PresentationCore", "System.Windows.Media.Effects", "SamplingMode"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.True(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.Collections.Specialized", "CollectionChangedEventManager")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows.Controls", "SpellCheck")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media.Effects", "ShaderEffect")));
        // Converted call shapes bind against the engine (member-level pins).
        var notify = typeof(System.Collections.Specialized.INotifyCollectionChanged);
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("AddHandler", [notify, typeof(System.Collections.Specialized.NotifyCollectionChangedEventHandler)]));
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("RemoveHandler", [notify, typeof(System.Collections.Specialized.NotifyCollectionChangedEventHandler)]));
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("AddListener", [notify, typeof(IWeakEventListener)]));
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("RemoveListener", [notify, typeof(IWeakEventListener)]));
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("StartListening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(CollectionChangedEventManager).GetMethod("StopListening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(ItemsChangedEventArgs).GetProperty("Action"));
        Assert.NotNull(typeof(ItemsChangedEventArgs).GetProperty("Position"));
        Assert.NotNull(typeof(ItemsChangedEventHandler).GetMethod("Invoke", [typeof(object), typeof(ItemsChangedEventArgs)]));
        Assert.NotNull(typeof(RequestBringIntoViewEventHandler).GetMethod("Invoke", [typeof(object), typeof(RequestBringIntoViewEventArgs)]));
        Assert.NotNull(typeof(RequestBringIntoViewEventArgs).GetProperty("TargetObject"));
        Assert.NotNull(typeof(RequestBringIntoViewEventArgs).GetProperty("TargetRect"));
        Assert.NotNull(typeof(FrameworkElement).GetField("RequestBringIntoViewEvent"));
        Assert.NotNull(typeof(FrameworkElement).GetEvent("RequestBringIntoView"));
        Assert.NotNull(typeof(FrameworkElement).GetMethod("OnRequestBringIntoView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(ItemContainerGenerator).GetEvent("ItemsChanged"));
        Assert.NotNull(typeof(ParserContext).GetConstructor([typeof(System.Xml.XmlParserContext)]));
        Assert.NotNull(typeof(ParserContext).GetProperty("XmlnsDictionary"));
        Assert.NotNull(typeof(ParserContext).GetMethod("ToXmlParserContext", [typeof(ParserContext)]));
        Assert.NotNull(typeof(XmlnsDictionary).GetMethod("LookupNamespace", [typeof(string)]));
        Assert.NotNull(typeof(SpellCheck).GetMethod("GetIsEnabled", [typeof(TextBoxBase)]));
        Assert.NotNull(typeof(SpellingError).GetProperty("Suggestions"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetProperty("ElementStyle"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetProperty("EditingElementStyle"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetProperty("SelectedItemBinding"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetProperty("TextBinding"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetField("ElementStyleProperty"));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetMethod("GenerateElement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetMethod("CancelCellEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(DataGridComboBoxColumn).GetMethod("CommitCellEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(DataGridCheckBoxColumn).GetProperty("IsThreeState"));
        Assert.NotNull(typeof(DataGridTemplateColumn).GetProperty("CellTemplate"));
        Assert.NotNull(typeof(DataGridTemplateColumn).GetProperty("CellEditingTemplateSelector"));
        Assert.NotNull(typeof(DataGridAutoGeneratingColumnEventArgs).GetConstructor([typeof(string), typeof(Type), typeof(DataGridColumn)]));
        Assert.NotNull(typeof(DataGridAutoGeneratingColumnEventArgs).GetProperty("Column"));
        Assert.NotNull(typeof(Quaternion).GetConstructor([typeof(Vector3D), typeof(double)]));
        Assert.NotNull(typeof(Quaternion).GetMethod("op_Multiply", [typeof(Quaternion), typeof(Quaternion)]));
        Assert.NotNull(typeof(QuaternionRotation3D).GetConstructor([typeof(Quaternion)]));
        Assert.NotNull(typeof(QuaternionRotation3D).GetProperty("Quaternion"));
        Assert.NotNull(typeof(Viewport2DVisual3D).GetMethod("GetIsVisualHostMaterial", [typeof(Material)]));
        Assert.NotNull(typeof(Viewport2DVisual3D).GetMethod("SetIsVisualHostMaterial", [typeof(Material), typeof(bool)]));
        Assert.NotNull(typeof(Transform3DCollection).GetMethod("Add", [typeof(Transform3D)]));
        Assert.NotNull(typeof(Model3DGroup).GetProperty("Children"));
        Assert.NotNull(typeof(MediaElement).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(MediaElement).GetProperty("Source"));
        Assert.NotNull(typeof(MediaElement).GetProperty("Volume"));
        Assert.NotNull(typeof(MediaElement).GetEvent("MediaEnded"));
        Assert.NotNull(typeof(MediaElement).GetEvent("MediaFailed"));
        Assert.NotNull(typeof(LayoutInformation).GetMethod("GetLayoutSlot", [typeof(FrameworkElement)]));
        Assert.NotNull(typeof(Int32AnimationUsingKeyFrames).GetProperty("KeyFrames"));
        Assert.NotNull(typeof(Int32Animation).GetProperty("To"));
        Assert.NotNull(typeof(Int32KeyFrame).GetProperty("Value"));
        Assert.NotNull(typeof(Int32KeyFrame).GetMethod("InterpolateValue", [typeof(int), typeof(double)]));
        Assert.NotNull(typeof(Int32KeyFrameCollection).GetMethod("Add", [typeof(Int32KeyFrame)]));
        Assert.NotNull(typeof(ThicknessAnimationUsingKeyFrames).GetProperty("IsAdditive"));
        Assert.NotNull(typeof(PointAnimation).GetProperty("From"));
        Assert.NotNull(typeof(PointAnimation).GetProperty("To"));
        Assert.NotNull(typeof(ManipulationStartingEventArgs).GetProperty("Mode"));
        Assert.NotNull(typeof(ManipulationStartingEventArgs).GetProperty("ManipulationContainer"));
        Assert.NotNull(typeof(ManipulationInertiaStartingEventArgs).GetProperty("InitialVelocities"));
        Assert.NotNull(typeof(ManipulationInertiaStartingEventArgs).GetProperty("TranslationBehavior"));
        Assert.NotNull(typeof(ManipulationInertiaStartingEventArgs).GetProperty("RotationBehavior"));
        Assert.NotNull(typeof(ManipulationVelocities).GetProperty("LinearVelocity"));
        Assert.NotNull(typeof(ManipulationVelocities).GetProperty("AngularVelocity"));
        Assert.NotNull(typeof(InertiaTranslationBehavior).GetProperty("InitialVelocity"));
        Assert.NotNull(typeof(InertiaTranslationBehavior).GetProperty("DesiredDeceleration"));
        Assert.NotNull(typeof(InertiaRotationBehavior).GetProperty("InitialVelocity"));
        Assert.NotNull(typeof(ShaderEffect).GetMethod("RegisterPixelShaderSamplerProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.NotNull(typeof(ShaderEffect).GetMethod("PixelShaderConstantCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        Assert.NotNull(typeof(ShaderEffect).GetMethod("UpdateShaderValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(ShaderEffect).GetField("PixelShaderProperty"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversSlice73()
    {
        // Slice-73 material-design remainder (member-evidenced with
        // Mono.Cecil against the upstream net8.0-windows
        // MaterialDesignThemes.Wpf.dll, MaterialDesignColors.dll and
        // Microsoft.Xaml.Behaviors.dll plus live 9.0.21 Presentation
        // Framework/Core/WindowsBase reflection): the date-picker pair,
        // the context-menu pair plus the FrameworkElement route, the
        // adorner placeholder, the cache hint, the color converter, the
        // focus manager, the size-change carrier, the static-resource
        // extension, the generic weak-event manager, the localization
        // pair, the annotation store and the navigation service. Every
        // System.Windows.* row rides the default namespace rule once the
        // engine declares it: zero Overrides needed.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls.Primitives", "DatePickerTextBox"),
            ("PresentationFramework", "System.Windows.Controls", "DatePickerFormat"),
            ("PresentationFramework", "System.Windows.Controls", "ContextMenuEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "ContextMenuEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "AdornedElementPlaceholder"),
            ("PresentationFramework", "System.Windows", "StaticResourceExtension"),
            ("WindowsBase", "System.Windows", "WeakEventManager`2"),
            ("PresentationCore", "System.Windows.Media", "CachingHint"),
            ("PresentationCore", "System.Windows.Media", "ColorConverter"),
            ("PresentationCore", "System.Windows.Input", "FocusManager"),
            ("PresentationCore", "System.Windows", "SizeChangedInfo"),
            ("PresentationCore", "System.Windows", "LocalizabilityAttribute"),
            ("PresentationCore", "System.Windows", "LocalizationCategory"),
            ("PresentationFramework", "System.Windows.Annotations", "Annotation"),
            ("PresentationFramework", "System.Windows.Navigation", "NavigationService"),
        })
            Assert.NotNull(WpfTypeMap.MapType(assembly, ns, name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationFramework", "System.Windows.Controls", "DatePickerTextBox")));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media", "ColorConverter")));
        // The Behaviors layout enumerators map through the nested-type rule
        // (non-generic outers, like the PathFigureCollection precedent).
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media.Animation", "TimelineCollection", ["Enumerator"]));
        Assert.NotNull(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "TransformCollection", ["Enumerator"]));
        // Converted call shapes bind against the engine (member-level pins).
        Assert.NotNull(typeof(ContextMenuEventHandler).GetMethod("Invoke", [typeof(object), typeof(ContextMenuEventArgs)]));
        Assert.NotNull(typeof(ContextMenuEventArgs).GetProperty("CursorLeft"));
        Assert.NotNull(typeof(ContextMenuEventArgs).GetProperty("CursorTop"));
        Assert.NotNull(typeof(ContextMenuEventArgs).GetProperty("TargetElement"));
        Assert.NotNull(typeof(FrameworkElement).GetField("ContextMenuOpeningEvent"));
        Assert.NotNull(typeof(FrameworkElement).GetField("ContextMenuClosingEvent"));
        Assert.NotNull(typeof(FrameworkElement).GetEvent("ContextMenuOpening"));
        Assert.NotNull(typeof(FrameworkElement).GetEvent("ContextMenuClosing"));
        Assert.NotNull(typeof(FrameworkElement).GetMethod("OnContextMenuOpening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(FrameworkElement).GetMethod("OnContextMenuClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(AdornedElementPlaceholder).GetProperty("AdornedElement"));
        Assert.NotNull(typeof(DatePickerTextBox).GetProperty("Watermark"));
        Assert.NotNull(typeof(DatePicker).GetProperty("SelectedDateFormat"));
        Assert.NotNull(typeof(DatePicker).GetField("SelectedDateFormatProperty"));
        Assert.NotNull(typeof(ColorConverter).GetMethod("ConvertFromString", [typeof(string)]));
        Assert.NotNull(typeof(ColorConverter).GetMethod("ConvertTo", [typeof(System.ComponentModel.ITypeDescriptorContext), typeof(System.Globalization.CultureInfo), typeof(object), typeof(Type)]));
        Assert.NotNull(typeof(FocusManager).GetMethod("GetFocusedElement", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(FocusManager).GetMethod("SetFocusedElement", [typeof(DependencyObject), typeof(IInputElement)]));
        Assert.NotNull(typeof(FocusManager).GetField("FocusedElementProperty"));
        Assert.NotNull(typeof(FocusManager).GetField("IsFocusScopeProperty"));
        Assert.NotNull(typeof(SizeChangedInfo).GetConstructor([typeof(UIElement), typeof(Size), typeof(bool), typeof(bool)]));
        Assert.NotNull(typeof(SizeChangedInfo).GetProperty("NewSize"));
        Assert.NotNull(typeof(StaticResourceExtension).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(StaticResourceExtension).GetConstructor([typeof(object)]));
        Assert.NotNull(typeof(StaticResourceExtension).GetProperty("ResourceKey"));
        Assert.NotNull(typeof(WeakEventManager<,>).GetMethod("AddHandler"));
        Assert.NotNull(typeof(WeakEventManager<,>).GetMethod("RemoveHandler"));
        Assert.NotNull(typeof(LocalizabilityAttribute).GetConstructor([typeof(LocalizationCategory)]));
        Assert.NotNull(typeof(Annotation).GetProperty("Anchors"));
        Assert.NotNull(typeof(Annotation).GetProperty("Authors"));
        Assert.NotNull(typeof(Annotation).GetEvent("AnchorChanged"));
        Assert.NotNull(typeof(Annotation).GetConstructor([typeof(System.Xml.XmlQualifiedName), typeof(Guid), typeof(DateTime), typeof(DateTime)]));
        Assert.NotNull(typeof(AnnotationResource).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(AnnotationResource).GetProperty("Name"));
        Assert.NotNull(typeof(Frame).GetProperty("NavigationService"));
        Assert.NotNull(typeof(NavigationService).GetMethod("Navigate", [typeof(object)]));
        Assert.NotNull(typeof(NavigationService).GetMethod("GoBack", Type.EmptyTypes));
        Assert.NotNull(typeof(NavigationService).GetProperty("CanGoBack"));
        Assert.NotNull(typeof(NavigationService).GetMethod("GetNavigationService", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(TimelineCollection).GetNestedType("Enumerator"));
        Assert.Same(typeof(TimelineCollection).GetNestedType("Enumerator"), typeof(TimelineCollection).GetMethod("GetEnumerator", Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(typeof(TransformCollection).GetNestedType("Enumerator"));
        Assert.Same(typeof(TransformCollection).GetNestedType("Enumerator"), typeof(TransformCollection).GetMethod("GetEnumerator", Type.EmptyTypes)!.ReturnType);
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversDataGridSlice()
    {
        // HandyControl lib DataGrid x7 (slice-79, verified member-by-member
        // against the upstream net10.0-windows HandyControl.dll with
        // Mono.Cecil): PropertyItemsControl..ctor calls
        // VirtualizingPanel.SetIsVirtualizingWhenGrouping(DependencyObject,
        // bool) plus SetScrollUnit(DependencyObject, ScrollUnit);
        // SelectAllButtonAttach calls MultiSelector.SelectAll/UnselectAll/
        // get_SelectedItems (isinst-guarded, ListBox fallback);
        // DataGridAttach adds/removes DataGrid.add_Sorting/remove_Sorting
        // with a DataGridSortingEventHandler over DataGridOnSorting (which
        // reads DataGridColumnEventArgs.get_Column and sets
        // DataGridSortingEventArgs.set_Handled), adds/removes
        // DataGrid.add_LoadingRow/remove_LoadingRow with an
        // EventHandler<DataGridRowEventArgs> over DataGrid_LoadingRow (which
        // reads DataGridRowEventArgs.get_Row), and calls
        // MultiSelector.UnselectAll plus DataGrid.CommitEdit on the grid
        // itself. All seven ride the default System.Windows.* namespace
        // rule once the engine declares them, so no Overrides entry is
        // needed (default-rule map without Overrides, like the
        // Media3D/DispatcherOperation slices).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "VirtualizingPanel"),
            ("PresentationFramework", "System.Windows.Controls", "ScrollUnit"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "MultiSelector"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridRowEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridSortingEventHandler"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridSortingEventArgs"),
            ("PresentationFramework", "System.Windows.Controls", "DataGridColumnEventArgs"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(VirtualizingPanel).IsSubclassOf(typeof(Panel)));
        Assert.True(typeof(VirtualizingPanel).IsAbstract);
        Assert.Equal(ScrollUnit.Item, (ScrollUnit)1);
        Assert.Equal(ScrollUnit.Pixel, (ScrollUnit)0);
        Assert.NotNull(typeof(VirtualizingPanel).GetField("ScrollUnitProperty"));
        Assert.NotNull(typeof(VirtualizingPanel).GetField("IsVirtualizingWhenGroupingProperty"));
        Assert.NotNull(typeof(VirtualizingPanel).GetMethod("SetScrollUnit", [typeof(DependencyObject), typeof(ScrollUnit)]));
        Assert.NotNull(typeof(VirtualizingPanel).GetMethod("GetScrollUnit", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(VirtualizingPanel).GetMethod("SetIsVirtualizingWhenGrouping", [typeof(DependencyObject), typeof(bool)]));
        Assert.NotNull(typeof(VirtualizingPanel).GetMethod("GetIsVirtualizingWhenGrouping", [typeof(DependencyObject)]));
        Assert.True(typeof(MultiSelector).IsSubclassOf(typeof(Selector)));
        Assert.True(typeof(MultiSelector).IsAbstract);
        Assert.NotNull(typeof(MultiSelector).GetMethod("SelectAll", Type.EmptyTypes));
        Assert.NotNull(typeof(MultiSelector).GetMethod("UnselectAll", Type.EmptyTypes));
        Assert.NotNull(typeof(MultiSelector).GetProperty("SelectedItems"));
        Assert.NotNull(typeof(DataGridRowEventArgs).GetConstructor([typeof(DataGridRow)]));
        Assert.NotNull(typeof(DataGridRowEventArgs).GetProperty("Row"));
        Assert.NotNull(typeof(DataGridColumnEventArgs).GetConstructor([typeof(DataGridColumn)]));
        Assert.NotNull(typeof(DataGridColumnEventArgs).GetProperty("Column"));
        Assert.True(typeof(DataGridSortingEventArgs).IsSubclassOf(typeof(DataGridColumnEventArgs)));
        Assert.NotNull(typeof(DataGridSortingEventArgs).GetConstructor([typeof(DataGridColumn)]));
        Assert.NotNull(typeof(DataGridSortingEventArgs).GetProperty("Handled"));
        Assert.NotNull(typeof(DataGridSortingEventHandler).GetConstructor([typeof(object), typeof(IntPtr)]));
        // The DataGrid members converted HandyControl code binds.
        Assert.NotNull(typeof(DataGrid).GetEvent("Sorting"));
        Assert.NotNull(typeof(DataGrid).GetEvent("LoadingRow"));
        Assert.NotNull(typeof(DataGrid).GetMethod("OnSorting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(DataGrid).GetMethod("OnLoadingRow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(DataGrid).GetMethod("SelectAll", Type.EmptyTypes));
        Assert.NotNull(typeof(DataGrid).GetMethod("UnselectAll", Type.EmptyTypes));
        Assert.NotNull(typeof(DataGrid).GetMethod("CommitEdit", Type.EmptyTypes));
        Assert.NotNull(typeof(DataGrid).GetProperty("SelectedItems"));
        Assert.NotNull(typeof(DataGrid).GetProperty("CanUserSortColumns"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversProvidersSlice()
    {
        // HandyControl lib ObjectDataProvider/TextSearch pair (slice-80,
        // verified member-by-member against the upstream net10.0-windows
        // HandyControl.dll with Mono.Cecil): HandyControl.Data.EnumDataProvider
        // derives ObjectDataProvider (ctor sets MethodName to GetValues;
        // set_Type appends the enum Type to MethodParameters and sets
        // ObjectType to Enum; set_UseAttributes switches ObjectType between
        // EnumDataProvider and Enum) and
        // HandyControl.Tools.Helper.BindingHelper.GetString reads
        // TextSearch.TextProperty via BindingOperations.SetBinding on a fresh
        // DependencyObject (TwoWay path binding with the value as Source,
        // then GetValue plus ClearBinding). Both ride the default
        // System.Windows.* namespace rule once the engine declares them, so
        // no Overrides entry is needed (default-rule map without Overrides,
        // like the Media3D/DataGrid/DispatcherOperation slices).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Data", "ObjectDataProvider"),
            ("PresentationFramework", "System.Windows.Controls", "TextSearch"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The DataSourceProvider base rides the same default rule
        // (WindowsBase is a source assembly and System.Windows.Data matches
        // the namespace prefix); the converted EnumDataProvider binds its
        // base chain through it.
        var baseMapped = WpfTypeMap.MapType("WindowsBase", "System.Windows.Data", "DataSourceProvider");
        Assert.NotNull(baseMapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DataSourceProvider"),
            (baseMapped.Value.Assembly, baseMapped.Value.Namespace, baseMapped.Value.Name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.Windows.Data", "DataSourceProvider")));
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(DataSourceProvider).IsAbstract);
        Assert.True(typeof(DataSourceProvider).IsSubclassOf(typeof(object)));
        Assert.NotNull(typeof(DataSourceProvider).GetProperty("Data"));
        Assert.NotNull(typeof(DataSourceProvider).GetProperty("Error"));
        Assert.NotNull(typeof(DataSourceProvider).GetProperty("IsInitialLoadEnabled"));
        Assert.NotNull(typeof(DataSourceProvider).GetMethod("Refresh", Type.EmptyTypes));
        Assert.NotNull(typeof(DataSourceProvider).GetMethod("InitialLoad", Type.EmptyTypes));
        Assert.NotNull(typeof(DataSourceProvider).GetMethod("DeferRefresh", Type.EmptyTypes));
        Assert.NotNull(typeof(DataSourceProvider).GetEvent("DataChanged"));
        Assert.True(typeof(System.ComponentModel.INotifyPropertyChanged).IsAssignableFrom(typeof(DataSourceProvider)));
        Assert.True(typeof(System.ComponentModel.ISupportInitialize).IsAssignableFrom(typeof(DataSourceProvider)));
        Assert.True(typeof(ObjectDataProvider).IsSubclassOf(typeof(DataSourceProvider)));
        Assert.NotNull(typeof(ObjectDataProvider).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("ConstructorParameters"));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("MethodParameters"));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("IsAsynchronous"));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("MethodName"));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("ObjectInstance"));
        Assert.NotNull(typeof(ObjectDataProvider).GetProperty("ObjectType"));
        Assert.NotNull(typeof(ObjectDataProvider).GetMethod("ShouldSerializeMethodParameters", Type.EmptyTypes));
        Assert.NotNull(typeof(ObjectDataProvider).GetMethod("ShouldSerializeObjectType", Type.EmptyTypes));
        Assert.True(typeof(TextSearch).IsSubclassOf(typeof(DependencyObject)));
        Assert.NotNull(typeof(TextSearch).GetField("TextProperty"));
        Assert.NotNull(typeof(TextSearch).GetField("TextPathProperty"));
        Assert.NotNull(typeof(TextSearch).GetMethod("GetText", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(TextSearch).GetMethod("SetText", [typeof(DependencyObject), typeof(string)]));
        Assert.NotNull(typeof(TextSearch).GetMethod("GetTextPath", [typeof(DependencyObject)]));
        Assert.NotNull(typeof(TextSearch).GetMethod("SetTextPath", [typeof(DependencyObject), typeof(string)]));
        // The exact converted BindingHelper shape binds: SetBinding accepts a
        // plain DependencyObject target with a TextSearch DP.
        Assert.NotNull(typeof(BindingOperations).GetMethod("SetBinding",
            [typeof(DependencyObject), typeof(DependencyProperty), typeof(BindingBase)]));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversKeySplineClockSlice()
    {
        // HandyControl lib KeySpline/IKeyFrameAnimation/Clock trio (slice-81,
        // verified member-by-member against the upstream net10.0-windows
        // HandyControl.dll with Mono.Cecil): SplineGeometryKeyFrame..cctor
        // calls KeySpline..ctor() and InterpolateValueCore calls
        // KeySpline.GetSplineProgress(double); GeometryAnimationUsingKeyFrames
        // implements IKeyFrameAnimation explicitly (get_KeyFrames plus
        // set_KeyFrames forwarding to its own accessors);
        // GeometryAnimation.GetCurrentValueCore reads Clock.get_CurrentIteration
        // plus get_CurrentProgress, GeometryAnimationBase.GetCurrentValue reads
        // get_CurrentState, and GeometryAnimationUsingKeyFrames.GetCurrentValueCore
        // reads get_CurrentTime. All ride the default System.Windows.* namespace
        // rule once the engine declares them, so no Overrides entry is needed
        // (default-rule map without Overrides, like the providers slice).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media.Animation", "KeySpline"),
            ("PresentationCore", "System.Windows.Media.Animation", "IKeyFrameAnimation"),
            ("PresentationCore", "System.Windows.Media.Animation", "Clock"),
            ("PresentationCore", "System.Windows.Media.Animation", "ClockController"),
            ("PresentationCore", "System.Windows.Media.Animation", "TimeSeekOrigin"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // ClockState already rode the default rule since slice-72 (declared in
        // Slice72Animation.cs with the live-verified 0/1/2 values).
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Media.Animation", "ClockState")));
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(KeySpline).IsSubclassOf(typeof(Freezable)));
        Assert.NotNull(typeof(KeySpline).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(KeySpline).GetConstructor([typeof(Point), typeof(Point)]));
        Assert.NotNull(typeof(KeySpline).GetConstructor([typeof(double), typeof(double), typeof(double), typeof(double)]));
        Assert.NotNull(typeof(KeySpline).GetProperty("ControlPoint1"));
        Assert.NotNull(typeof(KeySpline).GetProperty("ControlPoint2"));
        Assert.NotNull(typeof(KeySpline).GetMethod("GetSplineProgress", [typeof(double)]));
        Assert.True(typeof(IKeyFrameAnimation).IsInterface);
        Assert.NotNull(typeof(IKeyFrameAnimation).GetProperty("KeyFrames"));
        Assert.True(typeof(Clock).IsSubclassOf(typeof(DispatcherObject)));
        Assert.NotNull(typeof(Clock).GetProperty("Controller"));
        Assert.NotNull(typeof(Clock).GetProperty("CurrentGlobalSpeed"));
        Assert.NotNull(typeof(Clock).GetProperty("CurrentIteration"));
        Assert.NotNull(typeof(Clock).GetProperty("CurrentProgress"));
        Assert.NotNull(typeof(Clock).GetProperty("CurrentState"));
        Assert.NotNull(typeof(Clock).GetProperty("CurrentTime"));
        Assert.NotNull(typeof(Clock).GetProperty("IsPaused"));
        Assert.NotNull(typeof(Clock).GetProperty("HasControllableRoot"));
        Assert.NotNull(typeof(Clock).GetProperty("NaturalDuration"));
        Assert.NotNull(typeof(Clock).GetProperty("Parent"));
        Assert.NotNull(typeof(Clock).GetProperty("Timeline"));
        Assert.NotNull(typeof(Clock).GetEvent("Completed"));
        Assert.NotNull(typeof(Clock).GetEvent("CurrentGlobalSpeedInvalidated"));
        Assert.NotNull(typeof(Clock).GetEvent("CurrentStateInvalidated"));
        Assert.NotNull(typeof(Clock).GetEvent("CurrentTimeInvalidated"));
        Assert.NotNull(typeof(Clock).GetEvent("RemoveRequested"));
        Assert.NotNull(typeof(ClockController).GetProperty("Clock"));
        Assert.NotNull(typeof(ClockController).GetProperty("SpeedRatio"));
        Assert.NotNull(typeof(ClockController).GetMethod("Begin", Type.EmptyTypes));
        Assert.NotNull(typeof(ClockController).GetMethod("Pause", Type.EmptyTypes));
        Assert.NotNull(typeof(ClockController).GetMethod("Resume", Type.EmptyTypes));
        Assert.NotNull(typeof(ClockController).GetMethod("Seek", [typeof(TimeSpan), typeof(TimeSeekOrigin)]));
        Assert.NotNull(typeof(ClockController).GetMethod("SeekAlignedToLastTick", [typeof(TimeSpan), typeof(TimeSeekOrigin)]));
        Assert.NotNull(typeof(ClockController).GetMethod("SkipToFill", Type.EmptyTypes));
        Assert.NotNull(typeof(ClockController).GetMethod("Stop", Type.EmptyTypes));
        Assert.NotNull(typeof(ClockController).GetMethod("Remove", Type.EmptyTypes));
        Assert.Equal(TimeSeekOrigin.BeginTime, (TimeSeekOrigin)0);
        Assert.Equal(TimeSeekOrigin.Duration, (TimeSeekOrigin)1);
        // The UsingKeyFrames animations carry the explicit contract like WPF.
        Assert.True(typeof(IKeyFrameAnimation).IsAssignableFrom(typeof(DoubleAnimationUsingKeyFrames)));
        Assert.True(typeof(IKeyFrameAnimation).IsAssignableFrom(typeof(ObjectAnimationUsingKeyFrames)));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversMarkupTrioSlice()
    {
        // HandyControl lib IAddChild / StyleTypedPropertyAttribute /
        // AutoToolTipPlacement trio (slice-82, verified member-by-member
        // against the upstream net10.0-windows HandyControl.dll with
        // Mono.Cecil): GeometryAnimationUsingKeyFrames implements IAddChild
        // explicitly (interface-mapped AddChild/AddText), StepBar carries
        // [StyleTypedProperty(Property = "ItemContainerStyle",
        // StyleTargetType = typeof(StepBarItem))], and RangeSlider registers
        // an AutoToolTipPlacement DP defaulting to 0 (None) with an
        // AutoToolTipPrecision int DP. All three ride the default
        // System.Windows.* namespace rule once the engine declares them, so
        // no Overrides entry is needed (default-rule map without Overrides,
        // like the KeySpline/Clock slice).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Markup", "IAddChild"),
            ("PresentationFramework", "System.Windows", "StyleTypedPropertyAttribute"),
            ("PresentationFramework", "System.Windows.Controls.Primitives", "AutoToolTipPlacement"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(IAddChild).IsInterface);
        Assert.NotNull(typeof(IAddChild).GetMethod("AddChild", [typeof(object)]));
        Assert.NotNull(typeof(IAddChild).GetMethod("AddText", [typeof(string)]));
        Assert.True(typeof(StyleTypedPropertyAttribute).IsSealed);
        Assert.True(typeof(StyleTypedPropertyAttribute).IsSubclassOf(typeof(Attribute)));
        Assert.NotNull(typeof(StyleTypedPropertyAttribute).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(StyleTypedPropertyAttribute).GetProperty("Property"));
        Assert.NotNull(typeof(StyleTypedPropertyAttribute).GetProperty("StyleTargetType"));
        Assert.Equal(AttributeTargets.Class,
            ((AttributeUsageAttribute)Attribute.GetCustomAttribute(
                typeof(StyleTypedPropertyAttribute), typeof(AttributeUsageAttribute))!).ValidOn);
        Assert.True(((AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(StyleTypedPropertyAttribute), typeof(AttributeUsageAttribute))!).AllowMultiple);
        Assert.True(typeof(AutoToolTipPlacement).IsEnum);
        Assert.Equal(0, (int)AutoToolTipPlacement.None);
        Assert.Equal(1, (int)AutoToolTipPlacement.TopLeft);
        Assert.Equal(2, (int)AutoToolTipPlacement.BottomRight);
        // The exact converted RangeSlider DP shape binds: the engine DP
        // system carries the registration by name and type.
        Assert.NotNull(typeof(Slider).GetProperty("AutoToolTipPlacement"));
        Assert.NotNull(typeof(Slider).GetProperty("AutoToolTipPrecision"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversFreezableEnumeratorSlice()
    {
        // HandyControl lib nested FreezableCollection`1/Enumerator row
        // (slice-83, verified member-by-member against the upstream
        // net10.0-windows HandyControl.dll with Mono.Cecil): the lib's own
        // Interactivity behavior system iterates
        // FreezableCollection<Behavior/TriggerAction/TriggerBase/T> through
        // the compiler-generated foreach machinery (get_Current/MoveNext
        // calls, constrained prefixes and enumerator locals over
        // Enumerator<Behavior> style instantiations plus Enumerator<T> over
        // the generic parameter). The row rides the default System.Windows.*
        // rule once MapNestedType resolves nested types on generic outers,
        // so no Overrides entry is needed.
        var mapped = WpfTypeMap.MapType("PresentationCore", "System.Windows", "FreezableCollection`1");
        Assert.NotNull(mapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "FreezableCollection`1"),
            (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows", "FreezableCollection`1")));
        // Generic-outer nested resolution returns the open nested definition
        // (the referencing GenericInstanceType carries the instantiation).
        var nested = WpfTypeMap.MapNestedType("PresentationCore", "System.Windows", "FreezableCollection`1", ["Enumerator"]);
        Assert.NotNull(nested);
        Assert.True(nested!.IsValueType);
        Assert.Equal("Enumerator", nested.Name);
        Assert.Equal("FreezableCollection`1", nested.DeclaringType!.Name);
        // Unknown nested names still miss (the open-definition allowance
        // is scoped to nested types the engine actually declares).
        Assert.Null(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows", "FreezableCollection`1", ["Missing"]));
        Assert.Null(WpfTypeMap.MapNestedType("PresentationCore", "System.Windows.Media", "MediaPlayer", ["Enumerator"]));
        // The exact converted foreach call shapes bind on the engine
        // instantiation: GetEnumerator returns the nested struct and the
        // struct carries the MoveNext/get_Current members the converted
        // get_Current/MoveNext call operands target.
        var closed = typeof(FreezableCollection<VisualState>);
        var getEnumerator = closed.GetMethod("GetEnumerator", Type.EmptyTypes);
        Assert.NotNull(getEnumerator);
        Assert.True(getEnumerator!.ReturnType.IsValueType);
        Assert.Equal("Enumerator", getEnumerator.ReturnType.Name);
        Assert.NotNull(getEnumerator.ReturnType.GetMethod("MoveNext", Type.EmptyTypes));
        Assert.NotNull(getEnumerator.ReturnType.GetMethod("get_Current", Type.EmptyTypes));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversDragFeedbackSlice()
    {
        // AvalonEdit drag-feedback 7-row cluster (slice-85, verified
        // member-by-member against the upstream net10.0-windows
        // ICSharpCode.AvalonEdit.dll with Mono.Cecil plus the 10.0.13
        // PresentationCore ref pack and a live runtime probe):
        // SelectionMouseHandler.AttachDragDrop/DetachDragDrop wire
        // GiveFeedback/QueryContinueDrag handlers through the UIElement
        // add_/remove_ accessors, textArea_GiveFeedback flips
        // set_UseDefaultCursors, textArea_QueryContinueDrag reads
        // get_EscapePressed, and EditingCommandHandler copy paths construct
        // DataObjectCopying/SettingData args then RaiseEvent plus
        // get_CommandCancelled. All seven ride the default System.Windows.*
        // namespace rule once the engine declares them, so no Overrides
        // entry is needed (default-rule map without Overrides, like the
        // markup-trio slice). The Copying/SettingData handler delegates are
        // engine-side registration truth (DataObject.CopyingEvent /
        // SettingDataEvent carry them as handler types, like PastingEvent);
        // no upstream row references them.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows", "DragAction"),
            ("PresentationCore", "System.Windows", "GiveFeedbackEventArgs"),
            ("PresentationCore", "System.Windows", "GiveFeedbackEventHandler"),
            ("PresentationCore", "System.Windows", "QueryContinueDragEventArgs"),
            ("PresentationCore", "System.Windows", "QueryContinueDragEventHandler"),
            ("PresentationCore", "System.Windows", "DataObjectCopyingEventArgs"),
            ("PresentationCore", "System.Windows", "DataObjectSettingDataEventArgs"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // Exact converted call shapes resolve on the engine types.
        Assert.True(typeof(DragAction).IsEnum);
        Assert.Equal(0, (int)DragAction.Continue);
        Assert.Equal(1, (int)DragAction.Drop);
        Assert.Equal(2, (int)DragAction.Cancel);
        Assert.True(typeof(GiveFeedbackEventArgs).IsSealed);
        Assert.True(typeof(GiveFeedbackEventArgs).IsSubclassOf(typeof(RoutedEventArgs)));
        Assert.NotNull(typeof(GiveFeedbackEventArgs).GetProperty("Effects"));
        Assert.NotNull(typeof(GiveFeedbackEventArgs).GetProperty("UseDefaultCursors"));
        Assert.NotNull(typeof(GiveFeedbackEventHandler).GetMethod("Invoke",
            [typeof(object), typeof(GiveFeedbackEventArgs)]));
        Assert.True(typeof(QueryContinueDragEventArgs).IsSealed);
        Assert.NotNull(typeof(QueryContinueDragEventArgs).GetProperty("Action"));
        Assert.NotNull(typeof(QueryContinueDragEventArgs).GetProperty("EscapePressed"));
        Assert.NotNull(typeof(QueryContinueDragEventArgs).GetProperty("KeyStates"));
        Assert.NotNull(typeof(QueryContinueDragEventHandler).GetMethod("Invoke",
            [typeof(object), typeof(QueryContinueDragEventArgs)]));
        Assert.True(typeof(DataObjectCopyingEventArgs).IsSealed);
        Assert.True(typeof(DataObjectCopyingEventArgs).IsSubclassOf(typeof(DataObjectEventArgs)));
        Assert.NotNull(typeof(DataObjectCopyingEventArgs).GetConstructor([typeof(IDataObject), typeof(bool)]));
        Assert.NotNull(typeof(DataObjectCopyingEventArgs).GetProperty("DataObject"));
        Assert.True(typeof(DataObjectSettingDataEventArgs).IsSealed);
        Assert.NotNull(typeof(DataObjectSettingDataEventArgs).GetConstructor([typeof(IDataObject), typeof(string)]));
        Assert.NotNull(typeof(DataObjectSettingDataEventArgs).GetProperty("DataObject"));
        Assert.NotNull(typeof(DataObjectSettingDataEventArgs).GetProperty("Format"));
        // The converted UIElement add_/remove_ shapes bind onto the engine
        // CLR events (field-like events generate the accessors).
        Assert.Equal(typeof(GiveFeedbackEventHandler),
            typeof(UIElement).GetEvent("GiveFeedback")!.EventHandlerType);
        Assert.Equal(typeof(QueryContinueDragEventHandler),
            typeof(UIElement).GetEvent("QueryContinueDrag")!.EventHandlerType);
        // The converted RaiseEvent calls land on UIElement.RaiseEvent and
        // the veto reads land on DataObjectEventArgs.CommandCancelled.
        Assert.NotNull(typeof(UIElement).GetMethod("RaiseEvent", [typeof(RoutedEventArgs)]));
        Assert.NotNull(typeof(DataObjectEventArgs).GetProperty("CommandCancelled"));
        Assert.NotNull(typeof(DataObjectEventArgs).GetMethod("CancelCommand", Type.EmptyTypes));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversTextFormattingSlice()
    {
        // AvalonEdit text-formatting core (slice-86, verified
        // member-by-member against the upstream net10.0-windows
        // ICSharpCode.AvalonEdit.dll with Mono.Cecil plus the 10.0.13
        // PresentationCore ref pack and a live runtime probe): the editor
        // subclasses TextSource/TextRunProperties/TextParagraphProperties/
        // TextRunTypographyProperties/TextEmbeddedObject, drives lines
        // through TextFormatter.Create(TextFormattingMode).FormatLine with a
        // VisualLineTextSource, and consumes TextCharacters/TextEndOfParagraph
        // plus TextLine metrics/draw/hit-testing. All thirty ride the default
        // System.Windows.* rule once the engine declares them, so no
        // Overrides entry is needed (default-rule map without Overrides,
        // like the drag-feedback slice).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextFormatter"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextLine"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextRun"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextCharacters"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextEndOfParagraph"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextEmbeddedObject"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextEmbeddedObjectMetrics"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextSource"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextRunProperties"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextParagraphProperties"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextRunTypographyProperties"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextMarkerProperties"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextBounds"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextLineBreak"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "CharacterBufferRange"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "CharacterBufferReference"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "CharacterHit"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "TextSpan`1"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "CultureSpecificCharacterBufferRange"),
            ("PresentationCore", "System.Windows.Media.TextFormatting", "InvertAxes"),
            ("PresentationCore", "System.Windows", "BaselineAlignment"),
            ("PresentationCore", "System.Windows", "LineBreakCondition"),
            ("PresentationCore", "System.Windows", "FontVariants"),
            ("PresentationCore", "System.Windows", "FontNumeralStyle"),
            ("PresentationCore", "System.Windows", "FontNumeralAlignment"),
            ("PresentationCore", "System.Windows", "FontFraction"),
            ("PresentationCore", "System.Windows", "FontEastAsianWidths"),
            ("PresentationCore", "System.Windows", "FontEastAsianLanguage"),
            ("PresentationCore", "System.Windows", "FontCapitals"),
            ("PresentationCore", "System.Windows.Media", "TextEffectCollection"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The exact converted call shapes resolve on the engine types.
        Assert.NotNull(typeof(TextFormatter).GetMethod("Create", [typeof(TextFormattingMode)]));
        Assert.NotNull(typeof(TextFormatter).GetMethod("Create", Type.EmptyTypes));
        Assert.NotNull(typeof(TextFormatter).GetMethod("FormatLine",
            [typeof(TextSource), typeof(int), typeof(double), typeof(TextParagraphProperties), typeof(TextLineBreak)]));
        Assert.NotNull(typeof(TextCharacters).GetConstructor(
            [typeof(string), typeof(int), typeof(int), typeof(TextRunProperties)]));
        Assert.NotNull(typeof(TextEndOfParagraph).GetConstructor([typeof(int)]));
        Assert.NotNull(typeof(CharacterBufferRange).GetConstructor([typeof(string), typeof(int), typeof(int)]));
        Assert.NotNull(typeof(TextSpan<CultureSpecificCharacterBufferRange>).GetConstructor(
            [typeof(int), typeof(CultureSpecificCharacterBufferRange)]));
        Assert.NotNull(typeof(TextSource).GetMethod("GetTextRun", [typeof(int)]));
        Assert.NotNull(typeof(TextSource).GetMethod("GetPrecedingText", [typeof(int)]));
        Assert.NotNull(typeof(TextLine).GetMethod("Draw",
            [typeof(DrawingContext), typeof(Point), typeof(InvertAxes)]));
        Assert.NotNull(typeof(TextLine).GetMethod("GetTextRunSpans", Type.EmptyTypes));
        Assert.NotNull(typeof(TextLine).GetMethod("GetTextBounds", [typeof(int), typeof(int)]));
        // The converted override targets exist with the exact signatures.
        Assert.True(typeof(TextRun).GetProperty("Length")!.GetMethod!.IsAbstract);
        Assert.True(typeof(TextEmbeddedObject).GetProperty("BreakBefore")!.GetMethod!.IsAbstract);
        Assert.True(typeof(TextRunProperties).GetProperty("Typeface")!.GetMethod!.IsAbstract);
        Assert.True(typeof(TextParagraphProperties).GetProperty("DefaultTextRunProperties")!.GetMethod!.IsAbstract);
        Assert.Equal(43, typeof(TextRunTypographyProperties).GetProperties()
            .Count(p => p.GetMethod!.IsAbstract));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversUiAutomationSlice()
    {
        // AvalonEdit UIA text-provider cluster (slice-87, verified
        // member-by-member against the upstream net10.0-windows
        // ICSharpCode.AvalonEdit.dll with Mono.Cecil plus the 10.0.13
        // UIAutomationTypes/UIAutomationProvider/PresentationCore ref packs):
        // TextAreaAutomationPeer derives FrameworkElementAutomationPeer and
        // implements ITextProvider + IValueProvider (GetPattern Text/Value
        // switch, get_Provider via the protected-internal instance
        // ProviderFromPeer, OnSelectionChanged raising
        // TextPatternOnTextSelectionChanged),
        // TextEditorAutomationPeer implements IValueProvider explicitly
        // (IsReadOnly/Value/SetValue overrides plus RaiseIsReadOnlyChanged
        // through ValuePatternIdentifiers.IsReadOnlyProperty), and
        // TextRangeProvider implements ITextRangeProvider with TextUnit /
        // TextPatternRangeEndpoint parameters and IRawElementProviderSimple
        // children. All ten ride the default System.Windows.* rule once the
        // engine declares them, so no Overrides entry is needed
        // (default-rule map without Overrides, like the text-formatting
        // slice). The raise/provider members are engine-side registration
        // truth for the converted call shapes; there is no OS automation
        // tree in the browser, so raises record instead of dispatching.
        foreach (var (assembly, ns, name) in new[]
        {
            ("UIAutomationProvider", "System.Windows.Automation.Provider", "ITextProvider"),
            ("UIAutomationProvider", "System.Windows.Automation.Provider", "IValueProvider"),
            ("UIAutomationProvider", "System.Windows.Automation.Provider", "ITextRangeProvider"),
            ("UIAutomationProvider", "System.Windows.Automation.Provider", "IRawElementProviderSimple"),
            ("UIAutomationTypes", "System.Windows.Automation.Text", "TextPatternRangeEndpoint"),
            ("UIAutomationTypes", "System.Windows.Automation.Text", "TextUnit"),
            ("UIAutomationTypes", "System.Windows.Automation", "SupportedTextSelection"),
            ("UIAutomationTypes", "System.Windows.Automation", "AutomationProperty"),
            ("UIAutomationTypes", "System.Windows.Automation", "ValuePatternIdentifiers"),
            ("PresentationCore", "System.Windows.Automation.Peers", "AutomationEvents"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The exact converted call shapes resolve on the engine types.
        Assert.True(typeof(ITextProvider).IsInterface);
        Assert.NotNull(typeof(ITextProvider).GetMethod("GetSelection", Type.EmptyTypes));
        Assert.NotNull(typeof(ITextProvider).GetMethod("RangeFromChild", [typeof(IRawElementProviderSimple)]));
        Assert.Equal(typeof(SupportedTextSelection),
            typeof(ITextProvider).GetProperty("SupportedTextSelection")!.PropertyType);
        Assert.True(typeof(IValueProvider).IsInterface);
        Assert.NotNull(typeof(IValueProvider).GetMethod("SetValue", [typeof(string)]));
        Assert.True(typeof(ITextRangeProvider).IsInterface);
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("CompareEndpoints",
            [typeof(TextPatternRangeEndpoint), typeof(ITextRangeProvider), typeof(TextPatternRangeEndpoint)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("Move", [typeof(TextUnit), typeof(int)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("GetEnclosingElement", Type.EmptyTypes));
        Assert.Equal(30046, ValuePatternIdentifiers.IsReadOnlyProperty.Id);
        Assert.Equal(20, (int)AutomationEvents.ActiveTextPositionChanged);
        Assert.Equal(6, (int)TextUnit.Document);
        // The converted peer plumbing binds: protected-internal instance
        // ProviderFromPeer (the exact converted get_Provider call shape),
        // the two public raise methods, the static FromElement, and the
        // protected element slot the converted overrides target.
        Assert.NotNull(typeof(AutomationPeer).GetMethod("ProviderFromPeer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [typeof(AutomationPeer)]));
        Assert.NotNull(typeof(AutomationPeer).GetMethod("RaiseAutomationEvent", [typeof(AutomationEvents)]));
        Assert.NotNull(typeof(AutomationPeer).GetMethod("RaisePropertyChangedEvent",
            [typeof(AutomationProperty), typeof(object), typeof(object)]));
        Assert.NotNull(typeof(UIElementAutomationPeer).GetMethod("FromElement", [typeof(UIElement)]));
        Assert.NotNull(typeof(UIElement).GetMethod("OnCreateAutomationPeer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversScrollInfoSlice()
    {
        // AvalonEdit scrolling (slice-88, verified member-by-member against
        // the upstream net10.0-windows ICSharpCode.AvalonEdit.dll with
        // Mono.Cecil plus the 10.0.13 PresentationFramework ref pack):
        // Rendering.TextView and Editing.TextArea both implement
        // System.Windows.Controls.Primitives.IScrollInfo; TextEditor.ScrollTo
        // reads get_CanHorizontallyScroll; BackgroundGeometryBuilder reads
        // ExtentWidth/ViewportWidth; TextView.OnScrollChange reads
        // get_ScrollOwner and its explicit Line/Page/MouseWheel verbs forward
        // into SetVerticalOffset/SetHorizontalOffset; TextArea.ApplyScrollInfo
        // writes ScrollOwner/CanVerticallyScroll/CanHorizontallyScroll; and
        // CompletionWindowBase.TextViewScrollOffsetChanged reads the offsets
        // and viewports. The single interface rides the default
        // System.Windows.* rule once the engine declares it, so no Overrides
        // entry is needed (default-rule map without Overrides, like the
        // markup-trio slice). Engine ScrollViewer keeps pixel-based scrolling
        // and does not delegate to IScrollInfo children (CanContentScroll
        // logical scrolling stays a boundary); the converted TextArea/TextView
        // carry their own implementations.
        var mapped = WpfTypeMap.MapType(
            "PresentationFramework", "System.Windows.Controls.Primitives", "IScrollInfo");
        Assert.NotNull(mapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "IScrollInfo"),
            (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
        Assert.False(WpfTypeMap.Overrides.ContainsKey(
            ("PresentationFramework", "System.Windows.Controls.Primitives", "IScrollInfo")));
        // The exact converted call shapes resolve on the engine interface.
        var face = typeof(IScrollInfo);
        Assert.True(face.IsInterface);
        Assert.Equal(typeof(bool), face.GetProperty("CanHorizontallyScroll")!.PropertyType);
        Assert.NotNull(face.GetProperty("CanHorizontallyScroll")!.GetSetMethod());
        Assert.Equal(typeof(bool), face.GetProperty("CanVerticallyScroll")!.PropertyType);
        Assert.NotNull(face.GetProperty("CanVerticallyScroll")!.GetSetMethod());
        Assert.Equal(typeof(double), face.GetProperty("ExtentWidth")!.PropertyType);
        Assert.Equal(typeof(double), face.GetProperty("ExtentHeight")!.PropertyType);
        Assert.Equal(typeof(double), face.GetProperty("HorizontalOffset")!.PropertyType);
        Assert.Equal(typeof(double), face.GetProperty("VerticalOffset")!.PropertyType);
        Assert.Equal(typeof(double), face.GetProperty("ViewportWidth")!.PropertyType);
        Assert.Equal(typeof(double), face.GetProperty("ViewportHeight")!.PropertyType);
        Assert.Equal(typeof(ScrollViewer), face.GetProperty("ScrollOwner")!.PropertyType);
        Assert.NotNull(face.GetProperty("ScrollOwner")!.GetSetMethod());
        foreach (var verb in new[]
        {
            "LineUp", "LineDown", "LineLeft", "LineRight",
            "PageUp", "PageDown", "PageLeft", "PageRight",
            "MouseWheelUp", "MouseWheelDown", "MouseWheelLeft", "MouseWheelRight",
        })
            Assert.NotNull(face.GetMethod(verb, Type.EmptyTypes));
        Assert.NotNull(face.GetMethod("SetHorizontalOffset", [typeof(double)]));
        Assert.NotNull(face.GetMethod("SetVerticalOffset", [typeof(double)]));
        Assert.Equal(typeof(Rect),
            face.GetMethod("MakeVisible", [typeof(Visual), typeof(Rect)])!.ReturnType);
        // Every IScrollInfo member the upstream AvalonEdit binary references
        // resolves on the engine contract, and both converted implementers
        // target it: the precise TextArea/TextView surface this slice clears.
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var implementers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectScrollInfoRefs(type, referenced, implementers);
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.TextView", implementers);
        Assert.Contains("ICSharpCode.AvalonEdit.Editing.TextArea", implementers);
        Assert.NotEmpty(referenced);
        foreach (var member in referenced)
            Assert.NotNull(face.GetMember(member,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectScrollInfoRefs(
            TypeDefinition type, HashSet<string> referenced, HashSet<string> implementers)
        {
            foreach (var face in type.Interfaces)
            {
                if (face.InterfaceType.FullName == "System.Windows.Controls.Primitives.IScrollInfo")
                    implementers.Add(type.FullName);
            }
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    // Explicit implementation stubs (System.Windows.Controls.
                    // Primitives.IScrollInfo.* MethodDefs on TextView/TextArea)
                    // carry the interface name and call through it; every
                    // interface-scoped call target must resolve on the engine
                    // contract for the conversion to bind.
                    if (instruction.Operand is MethodReference called &&
                        called.DeclaringType.FullName == "System.Windows.Controls.Primitives.IScrollInfo")
                        referenced.Add(CecilNameToEngineMember(called.Name));
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectScrollInfoRefs(nested, referenced, implementers);
        }

        static string CecilNameToEngineMember(string name) =>
            name.StartsWith("get_", StringComparison.Ordinal) ? name.Substring(4) :
            name.StartsWith("set_", StringComparison.Ordinal) ? name.Substring(4) : name;
    }

    [Fact]
    public void MapCoversQueryCursorSlice()
    {
        // AvalonEdit cursor query (slice-89, verified member-by-member against
        // the upstream net10.0-windows ICSharpCode.AvalonEdit.dll with
        // Mono.Cecil plus the 10.0.13 PresentationCore ref pack and a live
        // 10.0.13 runtime probe): SelectionMouseHandler.Attach news up a
        // QueryCursorEventHandler(textArea_QueryCursor) and calls
        // UIElement.add_QueryCursor (Detach mirrors with remove_QueryCursor);
        // textArea_QueryCursor reads Handled and calls set_Cursor three times
        // (IBeam/Arrow) plus set_Handled; TextView.OnQueryCursor forwards into
        // VisualLineElement.OnQueryCursor (empty overridable base) and the
        // VisualLineLinkText override sets Handled plus Cursors.Hand. The pair
        // rides the default System.Windows.* rule once the engine declares it,
        // so no Overrides entry is needed (default-rule map without Overrides,
        // like the scrolling slice). The stylus-carrying args overload stays a
        // follow-up with StylusDevice; converted code never constructs args.
        foreach (var name in new[] { "QueryCursorEventArgs", "QueryCursorEventHandler" })
        {
            var mapped = WpfTypeMap.MapType("PresentationCore", "System.Windows.Input", name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", name)));
        }
        // The exact converted call shapes resolve on the engine pair.
        Assert.True(typeof(QueryCursorEventArgs).IsSubclassOf(typeof(MouseEventArgs)));
        Assert.Equal(typeof(Cursor), typeof(QueryCursorEventArgs).GetProperty("Cursor")!.PropertyType);
        Assert.NotNull(typeof(QueryCursorEventArgs).GetProperty("Cursor")!.GetSetMethod());
        var invoke = typeof(QueryCursorEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal([typeof(object), typeof(QueryCursorEventArgs)],
            invoke.GetParameters().Select(p => p.ParameterType).ToArray());
        var query = typeof(UIElement).GetEvent("QueryCursor")!;
        Assert.Equal(typeof(QueryCursorEventHandler), query!.EventHandlerType);
        Assert.NotNull(query.GetAddMethod());
        Assert.NotNull(query.GetRemoveMethod());
        var slot = typeof(UIElement).GetMethod("OnQueryCursor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            [typeof(QueryCursorEventArgs)])!;
        Assert.NotNull(slot);
        Assert.True(slot!.IsFamily);
        Assert.True(slot.IsVirtual);
        Assert.Equal("QueryCursor", UIElement.QueryCursorEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, UIElement.QueryCursorEvent.RoutingStrategy);
        // Every QueryCursor member the upstream AvalonEdit binary references
        // resolves on the engine contract, and the converted OnQueryCursor
        // overrides target the virtual engine slot: the precise
        // handler/override surface this slice clears.
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var overrides = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectQueryCursorRefs(type, referenced, overrides);
        Assert.Contains("set_Cursor", referenced);
        Assert.Contains("add_QueryCursor", referenced);
        Assert.Contains("remove_QueryCursor", referenced);
        Assert.Contains(".ctor", referenced);
        foreach (var member in referenced)
        {
            if (member == ".ctor") continue;
            var found =
                typeof(QueryCursorEventArgs).GetMember(member,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length != 0 ||
                typeof(UIElement).GetMember(member,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length != 0;
            Assert.True(found, $"engine contract is missing referenced member {member}");
        }
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.TextView", overrides);
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.VisualLineElement", overrides);
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.VisualLineLinkText", overrides);
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectQueryCursorRefs(
            TypeDefinition type, HashSet<string> referenced, HashSet<string> overrides)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name == "OnQueryCursor" && method.Parameters.Count == 1 &&
                    method.Parameters[0].ParameterType.FullName == "System.Windows.Input.QueryCursorEventArgs")
                    overrides.Add(type.FullName);
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference called &&
                        (called.DeclaringType.FullName == "System.Windows.Input.QueryCursorEventArgs" ||
                         called.DeclaringType.FullName == "System.Windows.Input.QueryCursorEventHandler" ||
                         (called.DeclaringType.FullName == "System.Windows.UIElement" &&
                          (called.Name == "add_QueryCursor" || called.Name == "remove_QueryCursor"))))
                        referenced.Add(called.Name);
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectQueryCursorRefs(nested, referenced, overrides);
        }
    }
    [Fact]
    public void MapCoversInputMethodSlice()
    {
        // AvalonEdit IME suspension + font converters (slice-90, verified
        // member-by-member against the upstream net10.0-windows
        // ICSharpCode.AvalonEdit.dll with Mono.Cecil plus the 10.0.13
        // PresentationCore runtime pack and a live 10.0.13 runtime probe):
        // Editing.ImeSupport..ctor and .TextAreaOptionChanged both call
        // InputMethod.SetIsInputMethodSuspended(textArea, bool) driven by
        // TextEditorOptions.EnableImeSupport; HighlightingColor/XshdColor/
        // V2Loader each new up FontStyleConverter/FontWeightConverter (each
        // reference is a .ctor call; ConvertFrom/ConvertTo flow through the
        // base TypeConverter virtuals). All three ride the default
        // System.Windows.* rule once the engine declares them, so no Overrides
        // entry is needed (default-rule map without Overrides, like the
        // scrolling/query slices). The OS IME context (Current, PreferredIme*
        // /ImeState/SpeechMode, StateChanged, InputScope) stays a follow-up;
        // the XAML setter path (ParseFontWeight/ParseFontStyle) is untouched.
        foreach (var (ns, name) in new[]
        {
            ("System.Windows.Input", "InputMethod"),
            ("System.Windows", "FontStyleConverter"),
            ("System.Windows", "FontWeightConverter"),
        })
        {
            var mapped = WpfTypeMap.MapType("PresentationCore", ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", ns, name)));
        }
        // The exact converted call shapes resolve on the engine contracts.
        var suspend = typeof(InputMethod).GetMethod("SetIsInputMethodSuspended",
            [typeof(DependencyObject), typeof(bool)])!;
        Assert.NotNull(suspend);
        Assert.True(suspend.IsStatic);
        Assert.Equal(typeof(void), suspend.ReturnType);
        Assert.NotNull(typeof(InputMethod).GetMethod("GetIsInputMethodSuspended",
            [typeof(DependencyObject)]));
        Assert.NotNull(typeof(InputMethod).GetMethod("SetIsInputMethodEnabled",
            [typeof(DependencyObject), typeof(bool)]));
        Assert.NotNull(typeof(InputMethod).GetMethod("GetIsInputMethodEnabled",
            [typeof(DependencyObject)]));
        Assert.NotNull(typeof(InputMethod).GetField("IsInputMethodEnabledProperty"));
        Assert.NotNull(typeof(InputMethod).GetField("IsInputMethodSuspendedProperty"));
        Assert.True(typeof(FontStyleConverter).IsSealed);
        Assert.True(typeof(FontWeightConverter).IsSealed);
        Assert.NotNull(typeof(FontStyleConverter).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(FontWeightConverter).GetConstructor(Type.EmptyTypes));
        // Every InputMethod/font-converter member the upstream AvalonEdit
        // binary references resolves on the engine contracts, from the exact
        // converted call sites: the ImeSupport suspend loop and the
        // highlighting converter constructions.
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var inputRefs = new SortedSet<string>(StringComparer.Ordinal);
        var converterRefs = new SortedSet<string>(StringComparer.Ordinal);
        var suspendUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectInputMethodRefs(type, inputRefs, converterRefs, suspendUsers);
        Assert.Equal(["SetIsInputMethodSuspended"], inputRefs);
        Assert.Contains("ICSharpCode.AvalonEdit.Editing.ImeSupport", suspendUsers);
        Assert.Equal(["System.Windows.FontStyleConverter:.ctor", "System.Windows.FontWeightConverter:.ctor"], converterRefs);
        var style = new FontStyleConverter();
        Assert.Equal(FontStyles.Italic, style.ConvertFromString("italic"));
        var weight = new FontWeightConverter();
        Assert.Equal(FontWeights.Bold, weight.ConvertFromString("Bold"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectInputMethodRefs(
            TypeDefinition type, SortedSet<string> inputRefs, SortedSet<string> converterRefs,
            HashSet<string> suspendUsers)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference called ||
                        called.DeclaringType is null) continue;
                    var owner = called.DeclaringType.FullName;
                    if (owner == "System.Windows.Input.InputMethod")
                    {
                        inputRefs.Add(called.Name);
                        if (called.Name == "SetIsInputMethodSuspended")
                            suspendUsers.Add(type.FullName);
                    }
                    else if (owner is "System.Windows.FontStyleConverter" or "System.Windows.FontWeightConverter")
                        converterRefs.Add(owner + ":" + called.Name);
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectInputMethodRefs(nested, inputRefs, converterRefs, suspendUsers);
        }
    }
    [Fact]
    public void MapCoversPropertyChangedSlice()
    {
        // AvalonEdit weak-event subscriptions (slice-91, verified
        // member-by-member against the upstream net10.0-windows
        // ICSharpCode.AvalonEdit.dll with Mono.Cecil plus the 10.0.13
        // WindowsBase runtime pack and a live 10.0.13 runtime probe):
        // TextEditor.OnDocumentChanged and LineNumberMargin.OnDocumentChanged
        // call PropertyChangedEventManager.AddListener/RemoveListener with
        // (INotifyPropertyChanged, IWeakEventListener, string). The manager
        // lives in System.ComponentModel, outside the default System.Windows.*
        // rule, so it needs an explicit Override onto the engine contract
        // (like CollectionChangedEventManager). The InputDevice/StylusDevice,
        // LanguageSpecificStringDictionary, LogicalDirection, InputManager,
        // and TextCompositionEventHandler rows stay follow-ups with their own
        // scopes; the 2 absent binaries stay declared.
        var mapped = WpfTypeMap.MapType("WindowsBase", "System.ComponentModel", "PropertyChangedEventManager");
        Assert.NotNull(mapped);
        Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", "PropertyChangedEventManager"),
            (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
        Assert.True(WpfTypeMap.Overrides.ContainsKey(("WindowsBase", "System.ComponentModel", "PropertyChangedEventManager")));
        // The exact converted call shapes resolve on the engine contract.
        foreach (var name in new[] { "AddListener", "RemoveListener" })
        {
            var method = typeof(PropertyChangedEventManager).GetMethod(name,
                [typeof(System.ComponentModel.INotifyPropertyChanged), typeof(IWeakEventListener), typeof(string)])!;
            Assert.NotNull(method);
            Assert.True(method.IsStatic);
            Assert.Equal(typeof(void), method.ReturnType);
        }
        foreach (var name in new[] { "AddHandler", "RemoveHandler" })
        {
            var method = typeof(PropertyChangedEventManager).GetMethod(name,
                [typeof(System.ComponentModel.INotifyPropertyChanged), typeof(System.EventHandler<System.ComponentModel.PropertyChangedEventArgs>), typeof(string)])!;
            Assert.NotNull(method);
            Assert.True(method.IsStatic);
        }
        Assert.True(typeof(PropertyChangedEventManager).IsSubclassOf(typeof(WeakEventManager)));
        Assert.False(typeof(PropertyChangedEventManager).IsSealed);
        // Every PropertyChangedEventManager member the upstream AvalonEdit
        // binary references resolves on the engine contract, from the exact
        // converted call sites (TextEditor/LineNumberMargin document hooks).
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var managerRefs = new SortedSet<string>(StringComparer.Ordinal);
        var managerUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectPropertyChangedRefs(type, managerRefs, managerUsers);
        Assert.Equal(["AddListener", "RemoveListener"], managerRefs);
        Assert.Contains("ICSharpCode.AvalonEdit.TextEditor", managerUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Editing.LineNumberMargin", managerUsers);
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectPropertyChangedRefs(
            TypeDefinition type, SortedSet<string> managerRefs, HashSet<string> managerUsers)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference called ||
                        called.DeclaringType is null) continue;
                    if (called.DeclaringType.FullName == "System.ComponentModel.PropertyChangedEventManager")
                    {
                        managerRefs.Add(called.Name);
                        managerUsers.Add(type.FullName);
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectPropertyChangedRefs(nested, managerRefs, managerUsers);
        }
    }

    [Fact]
    public void MapCoversInputDeviceSlice()
    {
        // AvalonEdit device pair (slice-92, verified member-by-member against
        // the upstream net10.0-windows ICSharpCode.AvalonEdit.dll with
        // Mono.Cecil plus the 10.0.13 PresentationCore ref pack):
        // TextView.RaiseHoverEventPair reads MouseEventArgs.get_StylusDevice
        // and news up MouseEventArgs through the (MouseDevice, int,
        // StylusDevice) overload; CompletionWindow.textArea_PreviewTextInput
        // and TextArea.PerformTextInput call InputEventArgs.get_Device and
        // pass the device into TextCompositionEventArgs..ctor (bound since
        // slice-93 through the composition scope, re-pinned below). Both ride the default
        // System.Windows.* rule once the engine declares them, so no
        // Overrides entry is needed (default-rule map without Overrides,
        // like the scrolling/query/input-method slices).
        foreach (var name in new[] { "InputDevice", "StylusDevice" })
        {
            var mapped = WpfTypeMap.MapType("PresentationCore", "System.Windows.Input", name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey(("PresentationCore", "System.Windows.Input", name)));
        }
        // The exact converted call shapes resolve on the engine contracts.
        Assert.True(typeof(InputDevice).IsAbstract);
        Assert.False(typeof(InputDevice).IsSealed);
        Assert.Same(typeof(DispatcherObject), typeof(InputDevice).BaseType);
        Assert.True(typeof(StylusDevice).IsSealed);
        Assert.Same(typeof(InputDevice), typeof(StylusDevice).BaseType);
        Assert.Same(typeof(InputDevice), typeof(MouseDevice).BaseType);
        Assert.Same(typeof(InputDevice), typeof(KeyboardDevice).BaseType);
        Assert.NotNull(typeof(InputEventArgs).GetProperty("Device"));
        Assert.Equal(typeof(InputDevice), typeof(InputEventArgs).GetProperty("Device")!.PropertyType);
        Assert.NotNull(typeof(MouseEventArgs).GetProperty("StylusDevice"));
        Assert.Equal(typeof(StylusDevice), typeof(MouseEventArgs).GetProperty("StylusDevice")!.PropertyType);
        Assert.NotNull(typeof(MouseEventArgs).GetConstructor([typeof(MouseDevice), typeof(int)]));
        Assert.NotNull(typeof(MouseEventArgs).GetConstructor(
            [typeof(MouseDevice), typeof(int), typeof(StylusDevice)]));
        Assert.NotNull(typeof(QueryCursorEventArgs).GetConstructor(
            [typeof(MouseDevice), typeof(int), typeof(StylusDevice)]));
        // Every InputDevice/StylusDevice member the upstream AvalonEdit
        // binary references resolves on the engine contracts, from the exact
        // converted call sites (RaiseHoverEventPair hover construction plus
        // the PreviewTextInput/PerformTextInput device reads). The
        // TextCompositionEventArgs..ctor(InputDevice, TextComposition) refs
        // bind since slice-93 (re-pinned to the advance below).
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var deviceRefs = new SortedSet<string>(StringComparer.Ordinal);
        var deviceUsers = new HashSet<string>(StringComparer.Ordinal);
        var stylusRefs = new SortedSet<string>(StringComparer.Ordinal);
        var stylusUsers = new HashSet<string>(StringComparer.Ordinal);
        var compositionCtorUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectInputDeviceRefs(type, deviceRefs, deviceUsers, stylusRefs, stylusUsers, compositionCtorUsers);
        Assert.Equal(["System.Windows.Input.InputEventArgs.get_Device"], deviceRefs);
        Assert.Single(deviceUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.CodeCompletion.CompletionWindow", deviceUsers);
        Assert.Equal([".ctor", "get_StylusDevice"], stylusRefs);
        Assert.Single(stylusUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.TextView", stylusUsers);
        Assert.Equal(
            ["ICSharpCode.AvalonEdit.CodeCompletion.CompletionWindow", "ICSharpCode.AvalonEdit.Editing.TextArea"],
            compositionCtorUsers.OrderBy(u => u, StringComparer.Ordinal).ToList());
        Assert.NotNull(typeof(TextCompositionEventArgs).GetConstructor(
            [typeof(InputDevice), typeof(TextComposition)]));
        Assert.NotNull(typeof(TextCompositionEventArgs).GetProperty("TextComposition"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectInputDeviceRefs(
            TypeDefinition type, SortedSet<string> deviceRefs, HashSet<string> deviceUsers,
            SortedSet<string> stylusRefs, HashSet<string> stylusUsers, HashSet<string> compositionCtorUsers)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference called ||
                        called.DeclaringType is null) continue;
                    var owner = called.DeclaringType.FullName;
                    if (owner == "System.Windows.Input.InputEventArgs" && called.Name == "get_Device")
                    {
                        deviceRefs.Add(owner + "." + called.Name);
                        deviceUsers.Add(type.FullName);
                    }
                    else if (owner == "System.Windows.Input.MouseEventArgs" &&
                        called.Name == ".ctor" && called.Parameters.Count == 3 &&
                        called.Parameters[2].ParameterType.FullName == "System.Windows.Input.StylusDevice")
                    {
                        stylusRefs.Add(called.Name);
                        stylusUsers.Add(type.FullName);
                    }
                    else if (owner == "System.Windows.Input.MouseEventArgs" &&
                        called.Name == "get_StylusDevice")
                    {
                        stylusRefs.Add(called.Name);
                        stylusUsers.Add(type.FullName);
                    }
                    else if (owner == "System.Windows.Input.TextCompositionEventArgs" &&
                        called is { Name: ".ctor", Parameters.Count: 2 } &&
                        called.Parameters[0].ParameterType.FullName == "System.Windows.Input.InputDevice")
                    {
                        compositionCtorUsers.Add(type.FullName);
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectInputDeviceRefs(nested, deviceRefs, deviceUsers, stylusRefs, stylusUsers, compositionCtorUsers);
        }
    }

    [Fact]
    public void MapCoversCompositionSlice()
    {
        // AvalonEdit composition scope (slice-93, verified member-by-member
        // against the upstream net10.0-windows ICSharpCode.AvalonEdit.dll with
        // Mono.Cecil plus the 10.0.13 PresentationCore/PresentationFramework
        // ref packs and live runtime probes): TextArea.add/remove_TextEntering/
        // TextEntered wire the handler type through UIElement-style add_/remove_
        // accessors with OnTextEntering/OnTextEntered invoking it;
        // CompletionWindow.AttachEvents/DetachEvents new up the handler and call
        // UIElement.add/remove_PreviewTextInput while textArea_PreviewTextInput
        // reads InputEventArgs.get_Device plus TextCompositionEventArgs.
        // get_TextComposition and news up TextCompositionEventArgs..ctor
        // (InputDevice, TextComposition); TextArea's two PerformTextInput
        // overloads call InputManager.get_Current() then new up
        // TextComposition..ctor(InputManager, IInputElement, string) and read
        // get_Text(); the VisualLine* GetNextCaretPosition overrides plus
        // TextRangeProvider.MoveOffset and TextUtilities.GetNextCaretPosition
        // take a LogicalDirection; LanguageSpecificStringDictionary survives
        // only as an orphaned module TypeRef (zero MemberRef users), and the
        // converter reports unmapped TypeRefs, so declaring the engine
        // contract clears the row. All four ride the default System.Windows.*
        // rule once the engine declares them, so no Overrides entry is needed.
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationCore", "System.Windows.Input", "TextCompositionEventHandler"),
            ("PresentationCore", "System.Windows.Input", "InputManager"),
            ("PresentationFramework", "System.Windows.Documents", "LogicalDirection"),
            ("PresentationCore", "System.Windows.Media", "LanguageSpecificStringDictionary"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The exact converted call shapes resolve on the engine contracts.
        Assert.Same(typeof(MulticastDelegate), typeof(TextCompositionEventHandler).BaseType);
        var invoke = typeof(TextCompositionEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke!.ReturnType);
        Assert.Equal(
            [typeof(object), typeof(TextCompositionEventArgs)],
            invoke.GetParameters().Select(p => p.ParameterType));
        Assert.True(typeof(InputManager).GetMethod("get_Current")!.IsStatic);
        Assert.NotNull(typeof(TextComposition).GetConstructor(
            [typeof(InputManager), typeof(IInputElement), typeof(string)]));
        Assert.NotNull(typeof(TextCompositionEventArgs).GetConstructor(
            [typeof(InputDevice), typeof(TextComposition)]));
        Assert.NotNull(typeof(TextCompositionEventArgs).GetProperty("Text"));
        Assert.NotNull(typeof(TextCompositionEventArgs).GetProperty("TextComposition"));
        Assert.NotNull(typeof(UIElement).GetMethod("add_PreviewTextInput"));
        Assert.NotNull(typeof(UIElement).GetMethod("remove_PreviewTextInput"));
        Assert.NotNull(typeof(UIElement).GetMethod("add_TextInput"));
        Assert.NotNull(typeof(UIElement).GetMethod("remove_TextInput"));
        Assert.NotNull(typeof(UIElement).GetField("PreviewTextInputEvent"));
        Assert.NotNull(typeof(UIElement).GetField("TextInputEvent"));
        Assert.NotNull(typeof(UIElement).GetMethod("OnPreviewTextInput",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(typeof(UIElement).GetMethod("OnTextInput",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.Equal(LogicalDirection.Backward, (LogicalDirection)0);
        Assert.Equal(LogicalDirection.Forward, (LogicalDirection)1);
        Assert.True(typeof(LanguageSpecificStringDictionary).IsSealed);
        // Every composition member the upstream AvalonEdit binary references
        // resolves on the engine contracts, from the exact converted call
        // sites; the dictionary TypeRef has zero member users by construction.
        using var upstream = AssemblyDefinition.ReadAssembly(UpstreamAvalonEditPath());
        var memberUsers = new HashSet<string>(StringComparer.Ordinal);
        var directionUsers = new HashSet<string>(StringComparer.Ordinal);
        var handlerUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectCompositionRefs(type, memberUsers, directionUsers, handlerUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Editing.TextArea", memberUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.CodeCompletion.CompletionWindow", memberUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Editing.TextArea", handlerUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.CodeCompletion.CompletionWindow", handlerUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Document.TextUtilities", directionUsers);
        Assert.Contains("ICSharpCode.AvalonEdit.Rendering.VisualLineElement", directionUsers);
        var dictionaryRefs = upstream.MainModule.GetTypeReferences()
            .Where(t => t.FullName == "System.Windows.Media.LanguageSpecificStringDictionary")
            .ToList();
        Assert.NotEmpty(dictionaryRefs);
        var dictionaryMemberUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in upstream.MainModule.Types)
            CollectMemberRefUsers(type, dictionaryMemberUsers,
                "System.Windows.Media.LanguageSpecificStringDictionary");
        Assert.Empty(dictionaryMemberUsers);
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());

        static void CollectCompositionRefs(
            TypeDefinition type, HashSet<string> memberUsers,
            HashSet<string> directionUsers, HashSet<string> handlerUsers)
        {
            foreach (var method in type.Methods)
            {
                foreach (var parameter in method.Parameters)
                {
                    if (parameter.ParameterType.FullName == "System.Windows.Documents.LogicalDirection")
                        directionUsers.Add(type.FullName);
                }
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference called ||
                        called.DeclaringType is null) continue;
                    var owner = called.DeclaringType.FullName;
                    if ((owner == "System.Windows.Input.InputManager" && called.Name == "get_Current") ||
                        (owner == "System.Windows.Input.TextComposition" && called.Name == ".ctor") ||
                        (owner == "System.Windows.Input.TextCompositionEventArgs" &&
                            (called.Name == ".ctor" || called.Name == "get_Text" ||
                                called.Name == "get_TextComposition")) ||
                        (owner == "System.Windows.UIElement" &&
                            (called.Name == "add_PreviewTextInput" ||
                                called.Name == "remove_PreviewTextInput" ||
                                called.Name == "OnTextInput" ||
                                called.Name == "OnPreviewTextInput")) ||
                        (owner == "System.Windows.Input.TextCompositionEventHandler" &&
                            called.Name == "Invoke"))
                        memberUsers.Add(type.FullName);
                    if (owner == "System.Windows.Input.TextCompositionEventHandler" &&
                        (called.Name == ".ctor" || called.Name == "Invoke"))
                        handlerUsers.Add(type.FullName);
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectCompositionRefs(nested, memberUsers, directionUsers, handlerUsers);
        }

        static void CollectMemberRefUsers(
            TypeDefinition type, HashSet<string> users, string declaringType)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MemberReference member &&
                        member.DeclaringType is not null &&
                        member.DeclaringType.FullName == declaringType)
                        users.Add(type.FullName + "::" + method.Name);
                }
            }
            foreach (var nested in type.NestedTypes)
                CollectMemberRefUsers(nested, users, declaringType);
        }
    }

    [Fact]
    public void MapCoversHandyFallbackSlice()
    {
        // Slice-96 HandyControl view-content fallback: SimplePanel /
        // TransitioningContentControl / TransitionMode ride the default
        // System.Windows.* rule once the engine declares them, so no
        // Overrides entry is needed (the converted HandyControl lib keeps its
        // own HandyControl.Controls identities; the engine shims serve
        // converted XAML short-name resolution, which bypasses the map).
        foreach (var (assembly, ns, name) in new[]
        {
            ("PresentationFramework", "System.Windows.Controls", "SimplePanel"),
            ("PresentationFramework", "System.Windows.Controls", "TransitioningContentControl"),
            ("PresentationFramework", "System.Windows.Controls", "TransitionMode"),
        })
        {
            var mapped = WpfTypeMap.MapType(assembly, ns, name);
            Assert.NotNull(mapped);
            Assert.Equal(("DesktopWasm.Wpf", "DesktopWasm.Wpf", name),
                (mapped.Value.Assembly, mapped.Value.Namespace, mapped.Value.Name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The engine fallback shapes the staged views bind against.
        Assert.True(typeof(SimplePanel).IsSubclassOf(typeof(Panel)));
        Assert.True(typeof(TransitioningContentControl).IsSubclassOf(typeof(ContentControl)));
        Assert.True(typeof(TransitionMode).IsEnum);
        Assert.NotNull(typeof(ScrollViewer).GetField("IsInertiaEnabledProperty",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    [Fact]
    public void MapCoversLangSlice()
    {
        // Slice-97 HandyControl language markup: ex:Lang / hc:Lang resolve
        // at LOAD time (xmlns to the converted LangExtension, values from
        // the staged Lang tables through the engine mirror), so the Convert
        // map needs no new Override and no re-package. The converted
        // HandyControl/HandyControlDemo Lang types live in non-framework
        // scopes and must pass through untouched; the engine mirror surface
        // below is what those heads bind to.
        foreach (var (assembly, ns, name) in new[]
        {
            ("HandyControl", "HandyControl.Tools.Extension", "LangExtension"),
            ("HandyControlDemo", "HandyControlDemo.Tools.Extension", "LangExtension"),
            ("HandyControl", "HandyControl.Properties.Langs", "LangProvider"),
            ("HandyControlDemo", "HandyControlDemo.Properties.Langs", "LangProvider"),
            ("HandyControl", "HandyControl.Properties.Langs", "LangKeys"),
            ("HandyControlDemo", "HandyControlDemo.Properties.Langs", "LangKeys"),
            ("HandyControl", "HandyControl.Interactivity", "ControlCommands"),
            ("HandyControl", "HandyControl.Controls", "IconElement"),
        })
        {
            Assert.Null(WpfTypeMap.MapType(assembly, ns, name));
            Assert.False(WpfTypeMap.Overrides.ContainsKey((assembly, ns, name)));
        }
        // The engine fallback shapes the converted XAML heads bind to.
        Assert.True(typeof(DesktopWasm.Wpf.LangExtension).IsSubclassOf(typeof(DesktopWasm.Wpf.MarkupExtension)));
        Assert.NotNull(typeof(DesktopWasm.Wpf.LangExtension).GetProperty("Key"));
        Assert.NotNull(typeof(DesktopWasm.Wpf.LangExtension).GetProperty("Source"));
        Assert.NotNull(typeof(DesktopWasm.Wpf.LangExtension).GetMethod("ProvideValue"));
        Assert.Empty(WpfTypeMap.ValidateEngineTargets());
    }

    private static string UpstreamAvalonEditPath()
    {
        // Receipt chain: the packager converts the pinned checkout tree, so
        // the upstream member evidence is proven against the same binary.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
            "targets", "studio-inputs", "handycontrol", "src", "Net_GE45",
            "HandyControlDemo_Net_GE45", "bin", "Release", "net10.0-windows",
            "ICSharpCode.AvalonEdit.dll")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName,
            "targets", "studio-inputs", "handycontrol", "src", "Net_GE45",
            "HandyControlDemo_Net_GE45", "bin", "Release", "net10.0-windows",
            "ICSharpCode.AvalonEdit.dll");
    }

    private string NewEmptySourceDir()
    {
        var dir = Path.Combine(_workDir, "empty-src");
        Directory.CreateDirectory(dir);
        // The embed gate needs at least one loose page; ballast fixtures
        // carry no real UI, so a detached Grid suffices.
        File.WriteAllText(Path.Combine(dir, "Dummy.xaml"),
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>");
        return dir;
    }

    private static string Sha256Of(byte[] bytes) =>
        System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>
    /// Chains the engine base constructor. The call targets the fake
    /// framework base; conversion rebinds it onto the engine base.
    /// </summary>
    private static void EmitBaseCtor(ModuleDefinition module, MethodDefinition ctor, TypeReference fakeBase)
    {
        var baseCtor = new MethodReference(".ctor", module.TypeSystem.Void, fakeBase)
        {
            HasThis = true,
        };
        var il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, baseCtor);
        il.Emit(OpCodes.Ret);
    }
}
