using System.Reflection;

namespace DesktopWasm.Convert;

/// <summary>
/// Name-based shim map, v1 (DesktopWasm.Convert map "wpf-convert-v1").
///
/// Real WPF apps reference PresentationFramework/WindowsBase/System.Xaml
/// (namespace System.Windows.*). The browser engine keeps the same public
/// type names in the DesktopWasm.Wpf assembly (Window, Application, Grid,
/// ...), so the converter rewrites matching references by name. Anything
/// without an engine counterpart is a conversion gap and fails the build
/// with a precise list instead of silently dropping behavior.
///
/// Namespace rule: System.Windows[.X] maps to the engine namespace when the
/// engine actually declares that type name; the map below pins the exact
/// assembly + namespace targets so a silent engine rename breaks conversion
/// loudly instead of binding somewhere unexpected.
/// </summary>
public static class WpfTypeMap
{
    public const string MapVersion = Wpf.WpfAppBoot.MapVersion;

    private const string EngineAssembly = "DesktopWasm.Wpf";
    private const string EngineNamespace = "DesktopWasm.Wpf";

    /// <summary>WPF assemblies whose references the converter rewrites.</summary>
    public static IReadOnlySet<string> SourceAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml",
        "UIAutomationTypes",
        "UIAutomationProvider",
        // Gallery MainWindow/HeaderTile subscribe to SystemEvents (the OS
        // broadcast becomes the host-driven RaiseUserPreferenceChanged
        // adapter in the engine's Microsoft.Win32 surface).
        "Microsoft.Win32.SystemEvents",
        // The Hosting EventLog logger's closure references EventLog types;
        // the framework payload is Windows-only, so the engine carries the
        // contract (construction retained, writes a precise boundary).
        "System.Diagnostics.EventLog",
        // ScreenToGif prerequisites (slice-30 platform shims): WinForms
        // window/screen/timer services, mutex ACL retention, AES session
        // protection, and the single CodeDom return-slot type. All are
        // absent from upstream build output (shared-framework/desktop-only),
        // so without these entries every live reference is a package-level
        // missing-dependency gap.
        "System.Windows.Forms",
        "System.Threading.AccessControl",
        "System.Security.Cryptography.ProtectedData",
        "System.CodeDom",
        // OxyPlot view prerequisites (slice-48 document/print/settings
        // shims): packaging, XPS document/print and designer-settings
        // assemblies referenced by OxyPlot.Wpf, all absent
        // from upstream build output. Each maps onto an engine counterpart
        // (PlatformShims.cs); device/packaging calls stay precise
        // NotSupported boundaries, like the EventLog slice.
        "System.IO.Packaging",
        "ReachFramework",
        "System.Printing",
        "System.Configuration.ConfigurationManager",
    };

    /// <summary>
    /// Explicit (source assembly, source namespace, source name) redirects.
    /// The default rule covers System.Windows.* names the engine declares;
    /// entries here handle names that moved or merged.
    /// </summary>
    public static IReadOnlyDictionary<(string Assembly, string Namespace, string Name), (string Assembly, string Namespace, string Name)> Overrides { get; } =
        new Dictionary<(string, string, string), (string, string, string)>
        {
            // Microsoft.Win32 dialogs live outside System.Windows.*, so the
            // default namespace rule cannot see them: each maps onto the
            // engine's Microsoft.Win32 surface (FileDialogs.cs).
            { ("PresentationFramework", "Microsoft.Win32", "CommonDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "CommonDialog") },
            { ("PresentationFramework", "Microsoft.Win32", "FileDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "FileDialog") },
            { ("PresentationFramework", "Microsoft.Win32", "CommonItemDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "CommonItemDialog") },
            { ("PresentationFramework", "Microsoft.Win32", "OpenFileDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "OpenFileDialog") },
            { ("PresentationFramework", "Microsoft.Win32", "SaveFileDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "SaveFileDialog") },
            { ("PresentationFramework", "Microsoft.Win32", "OpenFolderDialog"), ("DesktopWasm.Wpf", "Microsoft.Win32", "OpenFolderDialog") },
            // Gallery MainWindow/HeaderTile preference subscriptions.
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "SystemEvents"), ("DesktopWasm.Wpf", "Microsoft.Win32", "SystemEvents") },
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventHandler"), ("DesktopWasm.Wpf", "Microsoft.Win32", "UserPreferenceChangedEventHandler") },
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceChangedEventArgs"), ("DesktopWasm.Wpf", "Microsoft.Win32", "UserPreferenceChangedEventArgs") },
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "UserPreferenceCategory"), ("DesktopWasm.Wpf", "Microsoft.Win32", "UserPreferenceCategory") },
            // ScreenToGif recorders suspend on power change (same assembly/namespace gap as the preference events).
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModeChangedEventArgs"), ("DesktopWasm.Wpf", "Microsoft.Win32", "PowerModeChangedEventArgs") },
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModeChangedEventHandler"), ("DesktopWasm.Wpf", "Microsoft.Win32", "PowerModeChangedEventHandler") },
            { ("Microsoft.Win32.SystemEvents", "Microsoft.Win32", "PowerModes"), ("DesktopWasm.Wpf", "Microsoft.Win32", "PowerModes") },
            // Hosting EventLog logger closure (Windows-only payload, never staged).
            { ("System.Diagnostics.EventLog", "System.Diagnostics", "EventLog"), ("DesktopWasm.Wpf", "System.Diagnostics", "EventLog") },
            { ("System.Diagnostics.EventLog", "System.Diagnostics", "EventInstance"), ("DesktopWasm.Wpf", "System.Diagnostics", "EventInstance") },
            { ("System.Diagnostics.EventLog", "System.Diagnostics", "EventLogEntryType"), ("DesktopWasm.Wpf", "System.Diagnostics", "EventLogEntryType") },
            // ScreenToGif VisualHelper reads design mode through
            // DependencyPropertyDescriptor.FromProperty(...).Metadata: the
            // namespace rule only covers System.Windows.*, so this needs an
            // explicit redirect onto the engine descriptor.
            { ("WindowsBase", "System.ComponentModel", "DependencyPropertyDescriptor"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DependencyPropertyDescriptor") },
            // WebcamControl/UserSettings query the design-mode marker off
            // System.ComponentModel.DesignerProperties (same namespace gap).
            { ("PresentationFramework", "System.ComponentModel", "DesignerProperties"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DesignerProperties") },
            // Slice-30 platform shims: every live ScreenToGif reference into
            // the four absent framework assemblies, verified member-by-member
            // against the staged binaries (Screen.FromHandle/FromRectangle,
            // Timer add/remove_Tick + Interval + Start/Stop, MutexSecurity
            // AddAccessRule + SetAccessControl, Protect/Unprotect, the
            // GetStronglyTypedClassCode return slot).
            { ("System.Windows.Forms", "System.Windows.Forms", "IWin32Window"), ("DesktopWasm.Wpf", "System.Windows.Forms", "IWin32Window") },
            { ("System.Windows.Forms", "System.Windows.Forms", "Keys"), ("DesktopWasm.Wpf", "System.Windows.Forms", "Keys") },
            { ("System.Windows.Forms", "System.Windows.Forms", "Screen"), ("DesktopWasm.Wpf", "System.Windows.Forms", "Screen") },
            { ("System.Windows.Forms", "System.Windows.Forms", "SystemInformation"), ("DesktopWasm.Wpf", "System.Windows.Forms", "SystemInformation") },
            { ("System.Windows.Forms", "System.Windows.Forms", "Timer"), ("DesktopWasm.Wpf", "System.Windows.Forms", "Timer") },
            { ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexRights"), ("DesktopWasm.Wpf", "System.Security.AccessControl", "MutexRights") },
            { ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexAccessRule"), ("DesktopWasm.Wpf", "System.Security.AccessControl", "MutexAccessRule") },
            { ("System.Threading.AccessControl", "System.Security.AccessControl", "MutexSecurity"), ("DesktopWasm.Wpf", "System.Security.AccessControl", "MutexSecurity") },
            { ("System.Threading.AccessControl", "System.Threading", "ThreadingAclExtensions"), ("DesktopWasm.Wpf", "System.Threading", "ThreadingAclExtensions") },
            { ("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography", "DataProtectionScope"), ("DesktopWasm.Wpf", "System.Security.Cryptography", "DataProtectionScope") },
            { ("System.Security.Cryptography.ProtectedData", "System.Security.Cryptography", "ProtectedData"), ("DesktopWasm.Wpf", "System.Security.Cryptography", "ProtectedData") },
            { ("System.CodeDom", "System.CodeDom", "CodeTypeDeclaration"), ("DesktopWasm.Wpf", "System.CodeDom", "CodeTypeDeclaration") },
            // OxyPlot ExampleBrowser binds its grouped example list through
            // CollectionViewSource.GetDefaultView (an ICollectionView), as do
            // MahApps.Metro demo/control and MaterialDesign demo/theme views:
            // System.ComponentModel lives outside the System.Windows.*
            // namespace rule, so the view contract needs an explicit redirect
            // (CollectionViewSource itself is System.Windows.Data and rides
            // the default namespace rule once the engine declares it).
            { ("WindowsBase", "System.ComponentModel", "ICollectionView"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "ICollectionView") },
            // ScreenToGif DataGridHelper sorts through ItemCollection
            // SortDescriptions and groups through PropertyGroupDescription;
            // System.ComponentModel lives outside the System.Windows.*
            // namespace rule, so these need explicit redirects.
            { ("WindowsBase", "System.ComponentModel", "SortDescription"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "SortDescription") },
            { ("WindowsBase", "System.ComponentModel", "SortDescriptionCollection"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "SortDescriptionCollection") },
            { ("WindowsBase", "System.ComponentModel", "GroupDescription"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "GroupDescription") },
            { ("WindowsBase", "System.ComponentModel", "ListSortDirection"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "ListSortDirection") },
            // MaterialDesign tree controls subscribe collection changes weakly
            // (TreeListViewItem/TreeListViewItemsCollection Add/RemoveHandler):
            // System.Collections.Specialized lives outside the
            // System.Windows.* namespace rule, so the manager needs an
            // explicit redirect (the BCL event/args types pass through
            // untouched; only the manager type is engine-owned).
            { ("WindowsBase", "System.Collections.Specialized", "CollectionChangedEventManager"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "CollectionChangedEventManager") },
            // AvalonEdit TextEditor/LineNumberMargin subscribe document and
            // option changes through AddListener/RemoveListener (slice-91):
            // System.ComponentModel lives outside the System.Windows.*
            // namespace rule, so the manager needs an explicit redirect (the
            // BCL INotifyPropertyChanged/PropertyChangedEventArgs types pass
            // through untouched; only the manager type is engine-owned).
            { ("WindowsBase", "System.ComponentModel", "PropertyChangedEventManager"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "PropertyChangedEventManager") },
            // OxyPlot view prerequisites (slice-48): every live OxyPlot.Wpf
            // reference into the four absent document/print/settings
            // assemblies, verified member-by-member against the staged
            // net8.0-windows binaries (Package.Open(Stream,FileMode,FileAccess);
            // XpsDocument(Package)/CreateXpsDocumentWriter; inherited
            // Write(Visual); PrintQueue.CreateXpsDocumentWriter(ref area);
            // PrintDocumentImageableArea extents; SettingsBase.Synchronized;
            // ApplicationSettingsBase ()).
            { ("System.IO.Packaging", "System.IO.Packaging", "Package"), ("DesktopWasm.Wpf", "System.IO.Packaging", "Package") },
            { ("ReachFramework", "System.Windows.Xps.Packaging", "XpsDocument"), ("DesktopWasm.Wpf", "System.Windows.Xps.Packaging", "XpsDocument") },
            { ("System.Printing", "System.Windows.Xps", "XpsDocumentWriter"), ("DesktopWasm.Wpf", "System.Windows.Xps", "XpsDocumentWriter") },
            { ("System.Printing", "System.Printing", "PrintQueue"), ("DesktopWasm.Wpf", "System.Printing", "PrintQueue") },
            { ("System.Printing", "System.Printing", "PrintDocumentImageableArea"), ("DesktopWasm.Wpf", "System.Printing", "PrintDocumentImageableArea") },
            { ("System.Configuration.ConfigurationManager", "System.Configuration", "SettingsBase"), ("DesktopWasm.Wpf", "System.Configuration", "SettingsBase") },
            { ("System.Configuration.ConfigurationManager", "System.Configuration", "ApplicationSettingsBase"), ("DesktopWasm.Wpf", "System.Configuration", "ApplicationSettingsBase") },
            // MaterialDesignDemo reads documentation URLs through
            // ConfigurationManager.AppSettings (directive item P3a): the facade
            // lives outside the System.Windows.* namespace rule, so the static
            // class needs an explicit redirect onto the engine scoped adapter
            // (values seeded from the real upstream .dll.config at package time).
            { ("System.Configuration.ConfigurationManager", "System.Configuration", "ConfigurationManager"), ("DesktopWasm.Wpf", "System.Configuration", "ConfigurationManager") },
            // ScreenToGif.Util.SimpleKeyGesture carries this annotation naming
            // the framework KeyGestureValueSerializer (slice-55): the attribute
            // ships in System.ObjectModel on modern TFMs (forwarded from
            // WindowsBase), outside the SourceAssemblies/default-rule scopes,
            // so it needs an explicit redirect onto the engine marker.
            { ("System.ObjectModel", "System.Windows.Markup", "ValueSerializerAttribute"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "ValueSerializerAttribute") },
            // MaterialDesignThemes.Wpf posts through DispatcherExtensions.Invoke
            // (slice-69): the reference arrives under the
            // System.Windows.Presentation facade scope (net8.0-windows), which
            // is not a convertible source assembly, so the facade call needs
            // an explicit redirect onto the engine helpers.
            { ("System.Windows.Presentation", "System.Windows.Threading", "DispatcherExtensions"), ("DesktopWasm.Wpf", "DesktopWasm.Wpf", "DispatcherExtensions") },
            // HandyControlDemo ChatBoxViewModel.ReadMessage plays chat audio
            // through SoundPlayer..ctor(string)/PlaySync (slice-76, verified
            // member-by-member against the upstream net10.0-windows
            // HandyControlDemo.dll with Mono.Cecil; the only
            // System.Windows.Extensions reference in the entry): System.Media
            // lives outside the System.Windows.* namespace rule, so the
            // facade type needs an explicit redirect onto the engine player.
            { ("System.Windows.Extensions", "System.Media", "SoundPlayer"), ("DesktopWasm.Wpf", "System.Media", "SoundPlayer") },
        };

    /// <summary>
    /// Maps one referenced WPF type to its engine target.
    /// Returns null when the engine declares no such type (a gap).
    /// </summary>
    public static (string Assembly, string Namespace, string Name)? MapType(string sourceAssembly, string sourceNamespace, string sourceName)
    {
        if (Overrides.TryGetValue((sourceAssembly, sourceNamespace, sourceName), out var target))
            return target;
        // System.Windows.Input.ICommand moved out of the WPF assemblies
        // (System.ObjectModel on modern TFMs, netstandard for portable
        // libraries, PresentationCore on .NET Framework): it maps from any
        // scope because the engine owns the command contract converted apps
        // bind against (CommandBinding ctors, ButtonBase/MenuItem Command).
        if (sourceNamespace.Equals("System.Windows.Input", StringComparison.Ordinal) &&
            sourceName.Equals("ICommand", StringComparison.Ordinal))
            return (EngineAssembly, EngineNamespace, "ICommand");
        if (!SourceAssemblies.Contains(sourceAssembly)) return null;
        if (!sourceNamespace.Equals("System.Windows", StringComparison.Ordinal) &&
            !sourceNamespace.StartsWith("System.Windows.", StringComparison.Ordinal))
            return null;
        var engineType = typeof(Wpf.Window).Assembly.GetType($"{EngineNamespace}.{sourceName}", throwOnError: false);
        if (engineType is null || !string.Equals(engineType.Namespace, EngineNamespace, StringComparison.Ordinal))
            return null;
        return (EngineAssembly, EngineNamespace, sourceName);
    }

    /// <summary>
    /// Maps a nested framework type: allowlist first, engine RuntimeType second.
    /// The outermost declaring type goes through <see cref="MapType"/>; each
    /// inner name must exist as a nested type on the mapped engine type
    /// (e.g. PathFigureCollection/Enumerator, which the engine declares to
    /// match the WPF collection enumerators converted apps iterate).
    /// A nested type on a generic outer (e.g.
    /// FreezableCollection&lt;T&gt;/Enumerator) resolves to the open nested
    /// definition: the referencing GenericInstanceType carries the
    /// instantiation, so the rewrite mirrors the input shape (nested element
    /// under the engine outer definition plus rewritten generic arguments).
    /// Returns the resolved engine RuntimeType, or null when any level misses
    /// (including an open nested type whose generic parameters do not all
    /// come from the mapped outer definition).
    /// </summary>
    public static Type? MapNestedType(string sourceAssembly, string sourceNamespace, string outerName, IReadOnlyList<string> nestedNames)
    {
        var mapped = MapType(sourceAssembly, sourceNamespace, outerName);
        if (mapped is null || nestedNames.Count == 0) return null;
        var outer = typeof(Wpf.Window).Assembly.GetType($"{mapped.Value.Namespace}.{mapped.Value.Name}", throwOnError: false);
        if (outer is null) return null;
        var current = outer;
        foreach (var nested in nestedNames)
        {
            var next = current.GetNestedType(nested, BindingFlags.Public | BindingFlags.NonPublic);
            if (next is null) return null;
            current = next;
        }
        // The nested definition may stay open (generic outer): every generic
        // parameter must be position-scoped to the outer definition (nested
        // definitions redeclare the outer parameters at the same positions,
        // so position < outer arity means outer-scoped; a nested type with
        // its own parameters carries higher positions and still misses).
        if (current.ContainsGenericParameters &&
            !current.GetGenericArguments().All(a => a.IsGenericParameter && a.GenericParameterPosition < outer.GetGenericArguments().Length))
            return null;
        return current;
    }

    /// <summary>
    /// Validates that every type the map CAN produce actually resolves in
    /// the engine (catches engine renames breaking conversion silently).
    /// </summary>
    public static IReadOnlyList<string> ValidateEngineTargets()
    {
        var problems = new List<string>();
        foreach (var kv in Overrides.Values)
        {
            if (typeof(Wpf.Window).Assembly.GetType($"{kv.Namespace}.{kv.Name}", throwOnError: false) is null)
                problems.Add($"{kv.Assembly}:{kv.Namespace}.{kv.Name}");
        }
        // Probe the engine namespace surface the default rule relies on.
        foreach (var name in new[] { "Application", "Window", "Grid", "StackPanel", "TextBlock", "TextBox", "Button", "Canvas", "DockPanel", "Border", "ScrollViewer", "ListBox", "ListBoxItem", "TreeView", "TreeViewItem", "Menu", "MenuItem", "DataGrid",
            "ItemsControl", "Selector", "HeaderedItemsControl", "ComboBox", "ComboBoxItem", "ListView", "ListViewItem",
            "ItemCollection", "ItemContainerGenerator", "CollectionView", "ListCollectionView", "ICollectionView", "CollectionViewSource",
            "SelectionChangedEventArgs", "SelectionChangedEventHandler", "SelectionMode",
            "Binding", "BindingBase", "BindingExpression", "BindingMode", "IValueConverter", "UpdateSourceTrigger",
            "GroupDescription", "PropertyGroupDescription",
            "Orientation", "ResizeMode", "WindowStyle", "WindowState", "WindowStartupLocation", "SystemParameters", "ContentControl", "ButtonBase", "MessageBox", "MessageBoxResult", "MessageBoxButton", "MessageBoxImage", "RoutedEventArgs", "RoutedEventHandler", "TextCompositionEventArgs", "KeyboardFocusChangedEventArgs", "IComponentConnector", "Visibility", "ToggleButton", "CheckBox", "RadioButton", "Page", "Frame", "NavigationUIVisibility", "Duration", "RepeatBehavior", "Timeline", "TimelineCollection", "DoubleAnimation", "Storyboard", "IEasingFunction", "FillBehavior", "AnimationTimeline", "AnimationClock", "EasingMode", "EasingFunctionBase", "PowerEase", "HandoffBehavior", "KeyTimeType", "KeyTime", "ObjectKeyFrame", "DiscreteObjectKeyFrame", "ObjectKeyFrameCollection", "ObjectAnimationUsingKeyFrames", "ColorAnimationBase", "ColorAnimation", "ThicknessAnimation", "Color", "Point3D", "Vector3D", "Point3DCollection", "PointCollection", "Int32Collection", "Point", "Vector", "Size", "Rect", "Int32Rect", "Brush", "SolidColorBrush", "Colors", "Brushes", "PropertyChangedCallback", "DependencyPropertyChangedEventArgs", "DependencyPropertyKey", "Dispatcher", "DispatcherObject", "DispatcherPriority", "Key", "ModifierKeys", "InputDevice", "InputEventArgs", "KeyboardEventArgs", "KeyEventArgs", "KeyEventHandler", "KeyboardDevice", "Keyboard", "IInputElement", "KeyboardFocusChangedEventHandler", "PresentationSource", "TextComposition", "InputGesture", "InputGestureCollection", "KeyGesture", "KeyGestureConverter", "KeyConverter", "ModifierKeysConverter", "KeyInterop", "DispatcherOperation", "DispatcherOperation`1", "DispatcherOperationStatus", "DispatcherEventArgs", "DispatcherUnhandledExceptionEventArgs", "DispatcherUnhandledExceptionEventHandler", "DispatcherTimer", "ComponentDispatcher", "ThreadMessageEventArgs", "ThreadMessageEventHandler", "MouseEventArgs", "MouseEventHandler", "MouseButtonEventArgs", "MouseButtonEventHandler", "MouseWheelEventArgs", "MouseWheelEventHandler", "MouseDevice", "StylusDevice", "Mouse", "MouseButton", "MouseButtonState", "Cursor", "Cursors", "QueryCursorEventArgs", "QueryCursorEventHandler", "FocusNavigationDirection", "TraversalRequest", "RoutedCommand", "RoutedUICommand", "CommandBinding", "CommandBindingCollection", "CommandManager", "ExecutedRoutedEventArgs", "ExecutedRoutedEventHandler", "CanExecuteRoutedEventArgs", "CanExecuteRoutedEventHandler", "ApplicationCommands", "ComponentCommands", "NavigationCommands", "MediaCommands", "EditingCommands", "SystemCommands", "Control", "UserControl", "ControlTemplate", "TextBoxBase", "Style", "Setter", "SetterBase", "SetterBaseCollection", "EventSetter", "ResourceKey", "RoutedEvent", "RoutingStrategy", "EventManager", "ThemeMode", "FontFamily", "FontStyle", "FontWeight", "FontStyles", "FontWeights", "CornerRadius", "TextChangedEventArgs", "TextChangedEventHandler", "UndoAction", "ContentPropertyAttribute", "TemplatePartAttribute", "IStyleConnector", "Image", "ImageSource", "BitmapSource", "BitmapImage", "BitmapFrame", "BitmapDecoder", "GifBitmapDecoder", "BitmapEncoder", "PngBitmapEncoder", "JpegBitmapEncoder", "BmpBitmapEncoder", "BitmapMetadata", "ImageMetadata", "BitmapPalette", "BitmapSizeOptions", "BitmapCacheOption", "BitmapCreateOptions", "BitmapScalingMode", "PixelFormat", "PixelFormats", "WriteableBitmap", "CroppedBitmap", "FormatConvertedBitmap", "TransformedBitmap", "RenderTargetBitmap", "Stretch", "EdgeMode", "Matrix", "GeneralTransform", "Transform", "TransformCollection", "TransformGroup", "TranslateTransform", "RotateTransform", "ScaleTransform", "MatrixTransform", "Visual", "DrawingVisual", "VisualTreeHelper", "RenderOptions", "Transform3D", "TranslateTransform3D", "ScaleTransform3D", "Rotation3D", "AxisAngleRotation3D", "RotateTransform3D", "Transform3DGroup", "Point3DAnimation", "Geometry3D", "MeshGeometry3D", "Material", "DiffuseMaterial", "Model3D", "GeometryModel3D", "Light", "AmbientLight", "DirectionalLight", "Visual3D", "Visual3DCollection", "UIElement3D", "ContainerUIElement3D", "ModelVisual3D", "Camera", "ProjectionCamera", "PerspectiveCamera", "Vector3DCollection", "RayHitTestResult", "RayMeshGeometry3DHitTestResult", "Viewport3D",
            "Path", "Rectangle", "Ellipse", "Viewbox", "StretchDirection", "ContentPresenter", "Span", "Run", "LineBreak",
            "FrameworkTemplate", "DataTemplate", "DataTemplateSelector", "UniformGrid", "AttachedPropertyBrowsableForTypeAttribute",
            "Hyperlink", "RequestNavigateEventArgs", "RequestNavigateEventHandler",
            "ValidationRule", "ValidationResult", "ValidationStep", "Validation", "ValidationError",
            "ValidationErrorEventArgs", "ValidationErrorEventAction",
            "SizeChangedEventArgs", "SizeChangedEventHandler",
            "ScrollChangedEventArgs", "ScrollChangedEventHandler",
            "NavigationWindow", "NavigationEventArgs", "NavigatedEventHandler", "TriggerBase", "Trigger", "TriggerCollection", "TriggerAction", "TriggerActionCollection", "EventTrigger", "BeginStoryboard",
            "Clipboard", "TextDataFormat", "ClipboardException",
            "DataFormats", "IDataObject", "DataObject", "DataObjectEventArgs", "DataObjectPastingEventArgs", "DataObjectPastingEventHandler",
            "DragDrop", "DragDropEffects", "DragDropKeyStates", "DragEventArgs", "DragEventHandler",
            "HwndSource", "HwndSourceHook", "HwndSourceParameters", "HwndTarget", "CompositionTarget",
            "WindowInteropHelper", "Imaging", "MSG",
            "AutomationPeer", "UIElementAutomationPeer",
            "AutomationNotificationKind", "AutomationNotificationProcessing",
            "AutomationHeadingLevel", "AutomationProperties",
            "RangeBase", "Slider", "TickPlacement", "RoutedPropertyChangedEventArgs`1", "RoutedPropertyChangedEventHandler`1",
            "TabControl", "TabItem", "TabPanel", "ProgressBar", "ToolTip", "ToolTipService",
            "TemplateBindingExtension", "TextWrapping", "ScrollBarVisibility", "StreamResourceInfo", "WindowChrome", "NonClientFrameEdges",
            "Decorator", "AdornerDecorator", "Adorner", "AdornerLayer",
            "DataGridColumn", "DataGridBoundColumn", "DataGridRow", "DataGridCell",
            "DataGridCellsPresenter", "DataGridDetailsPresenter", "DataGridCellInfo",
            "DataGridItemCollection", "SortDescription", "SortDescriptionCollection",
            "ListSortDirection", "LogicalTreeHelper",
            "PropertyPath", "BindingOperations", "BindingExpressionBase",
            "StartupEventArgs", "StartupEventHandler",
            "ExitEventArgs", "ExitEventHandler", "ShutdownMode",
            "WindowCollection",
            "RowDefinitionCollection", "ColumnDefinitionCollection",
            "HeaderedContentControl", "GroupBox",
            "Calendar", "CalendarSelectionMode", "CalendarMode", "CalendarDateRange",
            "CalendarBlackoutDatesCollection", "SelectedDatesCollection",
            "CalendarDateChangedEventArgs", "CalendarModeChangedEventArgs",
            "DatePicker", "DatePickerDateValidationErrorEventArgs", "RichTextBox",
            "XamlReader", "XamlWriter", "XamlParseException",
            "VisualCollection", "HitTestResult", "PointHitTestResult", "GeometryHitTestResult",
            "HitTestResultBehavior", "HitTestResultCallback", "HitTestFilterBehavior", "HitTestFilterCallback",
            "HitTestParameters", "PointHitTestParameters", "GeometryHitTestParameters",
            "ToleranceType", "IntersectionDetail",
            "SizeToContent", "TaskbarItemInfo", "ThumbButtonInfo", "ThumbButtonInfoCollection",
            "TaskbarItemProgressState", "ICommandSource",
            "GridSplitter", "GridResizeDirection", "GridResizeBehavior",
            "Effect", "DropShadowEffect", "RenderingBias", "RenderMode", "ContainerVisual",
            "PasswordBox", "WebBrowser",
            "DynamicResourceExtension", "MarkupExtension", "IProvideValueTarget",
            "Geometry", "LineGeometry", "RectangleGeometry", "EllipseGeometry",
            "GeometryGroup", "GeometryCollection", "PathGeometry", "PathFigure", "PathFigureCollection", "PathFigureCollection+Enumerator", "PathSegment", "PathSegmentCollection", "PathSegmentCollection+Enumerator",
            "LineSegment", "BezierSegment", "QuadraticBezierSegment", "ArcSegment", "PolyLineSegment", "PolyBezierSegment", "PolyQuadraticBezierSegment",
            "StreamGeometry", "StreamGeometryContext", "CombinedGeometry", "GeometryCombineMode", "FillRule", "SweepDirection",
            "Shape", "Line", "Polyline", "PointCollection", "PointCollection+Enumerator", "DoubleCollection", "DoubleCollection+Enumerator",
            "PenLineCap", "PenLineJoin", "DashStyle", "DashStyles", "ColorInterpolationMode", "DrawingImage",
            "FrameworkPropertyMetadataOptions", "UIPropertyMetadata", "DependencyPropertyChangedEventHandler", "CoerceValueCallback", "ValidateValueCallback",
            "FrameworkContentElement", "FlowDirection", "FontStretch", "FontStretches",
            "TextElement", "Inline", "Block", "Paragraph", "InlineCollection", "BlockCollection", "TextElementCollection`1",
            "FlowDocument", "FlowDocumentScrollViewer",
            "StylusShape", "RectangleStylusShape", "EllipseStylusShape", "StylusTip", "ApplicationGesture",
            "DrawingAttributes", "PropertyDataChangedEventArgs", "PropertyDataChangedEventHandler",
            "StylusPoint", "StylusPointCollection", "Stroke", "StrokeCollection",
            "StrokeCollectionChangedEventArgs", "StrokeCollectionChangedEventHandler",
            "DrawingAttributesReplacedEventArgs", "DrawingAttributesReplacedEventHandler",
            "StylusPointsReplacedEventArgs", "StylusPointsReplacedEventHandler",
            "StylusButtonState", "StylusButton", "StylusButtonEventArgs", "StylusButtonEventHandler",
            "InkCanvas", "InkCanvasEditingMode",
            "InkCanvasStrokeCollectedEventArgs", "InkCanvasStrokeCollectedEventHandler",
            "UIElementCollection",
            "TileMode", "BrushMappingMode", "AlignmentX", "AlignmentY", "Pen", "Drawing", "DrawingGroup", "DrawingCollection", "GeometryDrawing", "TileBrush", "DrawingBrush",
            "VisualBrush", "GradientStop", "GradientStopCollection", "GradientStopCollection+Enumerator", "GradientBrush", "LinearGradientBrush", "RadialGradientBrush",
            "MediaPlayer", "ExceptionEventArgs", "BaseCompatibilityPreferences", "BaseCompatibilityPreferences+HandleDispatcherRequestProcessingFailureOptions",
            "Typeface", "GlyphTypeface", "FormattedText", "DpiScale", "DpiChangedEventArgs", "DpiChangedEventHandler",
            "NumberSubstitution", "NumberCultureSource", "NumberSubstitutionMethod", "TextFormattingMode",
            "TextDecoration", "TextDecorationCollection", "TextDecorationLocation", "TextDecorationUnit", "TextDecorations",
            "DesignerProperties", "SystemFonts", "SystemFontResourceKey",
            "TextOptions", "ClearTypeHint", "SerializerWriter", "ValueConversionAttribute",
            "ManipulationDelta", "ManipulationStartedEventArgs", "ManipulationDeltaEventArgs",
            "ManipulationCompletedEventArgs",
            "ComponentResourceKey", "GeneratorStatus", "HwndHost", "ToolBar", "OverflowMode",
            "InputBinding", "KeyBinding", "InputBindingCollection",
            "MouseBinding", "MouseGesture", "MouseAction",
            "TouchEventArgs", "TouchDevice", "TouchPoint", "TouchPointCollection", "TouchAction",
            "DependencyPropertyHelper", "ValueSource", "BaseValueSource",
            "GridLengthConverter", "FrameworkElementFactory", "StyleSelector",
            "IconBitmapDecoder", "DispatcherOperationCallback",
            "MarkupExtensionReturnTypeAttribute",
            "VisualStateManager", "VisualState", "VisualStateGroup",
            "VisualStateChangedEventArgs", "VisualTransition",
            "TemplateVisualStateAttribute", "FreezableCollection`1",
            "KeySpline", "IKeyFrameAnimation", "Clock", "ClockState",
            "ClockController", "TimeSeekOrigin",
            "DatePickerTextBox", "DatePickerFormat",
            "ContextMenuEventHandler", "ContextMenuEventArgs",
            "AdornedElementPlaceholder", "CachingHint",
            "ColorConverter", "FocusManager", "SizeChangedInfo",
            "StaticResourceExtension", "WeakEventManager`2",
            "LocalizabilityAttribute", "LocalizationCategory",
            "Modifiability", "Readability",
            "Annotation", "AnnotationResource", "AnnotationAction",
            "AnnotationResourceChangedEventArgs", "AnnotationAuthorChangedEventArgs",
            "AnnotationResourceChangedEventHandler", "AnnotationAuthorChangedEventHandler",
            "NavigationService",
            "TimelineCollection+Enumerator", "TransformCollection+Enumerator",
            "SplashScreen",
            "DataSourceProvider", "ObjectDataProvider", "TextSearch",
            "IAddChild", "StyleTypedPropertyAttribute", "AutoToolTipPlacement",
            "DragAction", "GiveFeedbackEventArgs", "GiveFeedbackEventHandler",
            "QueryContinueDragEventArgs", "QueryContinueDragEventHandler",
            "DataObjectCopyingEventArgs", "DataObjectCopyingEventHandler",
            "DataObjectSettingDataEventArgs", "DataObjectSettingDataEventHandler",
            "TextFormatter", "TextLine", "TextRun", "TextCharacters",
            "TextEndOfParagraph", "TextEmbeddedObject", "TextEmbeddedObjectMetrics",
            "TextSource", "TextRunProperties", "TextParagraphProperties",
            "TextRunTypographyProperties", "TextMarkerProperties", "TextBounds",
            "TextLineBreak", "CharacterBufferRange", "CharacterBufferReference",
            "CharacterHit", "TextSpan`1", "CultureSpecificCharacterBufferRange",
            "InvertAxes", "BaselineAlignment", "LineBreakCondition",
            "FontVariants", "FontNumeralStyle", "FontNumeralAlignment",
            "FontFraction", "FontEastAsianWidths", "FontEastAsianLanguage",
            "FontCapitals", "TextEffectCollection",
            "TextUnit", "TextPatternRangeEndpoint", "SupportedTextSelection",
            "AutomationEvents", "ProviderOptions",
            "AutomationIdentifier", "AutomationProperty", "AutomationPattern",
            "ValuePatternIdentifiers",
            "ITextProvider", "ITextRangeProvider", "IValueProvider",
            "IRawElementProviderSimple",
            "IScrollInfo",
            "InputMethod", "FontStyleConverter", "FontWeightConverter",
            "PropertyChangedEventManager",
            "TextCompositionEventHandler", "InputManager", "LogicalDirection",
            "LanguageSpecificStringDictionary",
            "SimplePanel", "TransitioningContentControl", "TransitionMode", "LangExtension" })
        {
            if (typeof(Wpf.Window).Assembly.GetType($"{EngineNamespace}.{name}", throwOnError: false) is null)
                problems.Add($"{EngineAssembly}:{EngineNamespace}.{name}");
        }
        // Nested types on generic outers (FreezableCollection<T>/Enumerator,
        // slice-83): Assembly.GetType misses open generics through the "+"
        // syntax, so the nested definition is probed off the generic
        // definition instead.
        if (typeof(Wpf.FreezableCollection<>).GetNestedType("Enumerator") is null)
            problems.Add($"{EngineAssembly}:{EngineNamespace}.FreezableCollection`1+Enumerator");
        return problems;
    }
}
