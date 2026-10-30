using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DesktopWasm.Wpf;

/// <summary>Thrown for malformed XAML or unsupported constructs.</summary>
public sealed class XamlLoadException : Exception
{
    public XamlLoadException(string message) : base(message) { }

    public XamlLoadException(string message, int line, int position)
        : base($"{message} (line {line}, position {position})")
    {
        Line = line;
        Position = position;
    }

    public int Line { get; } = -1;
    public int Position { get; } = -1;
}

/// <summary>One parsed event attribute (Click="Handler") awaiting codebehind binding.</summary>
public sealed record XamlEventWire(object Element, string EventName, string HandlerName);

/// <summary>Loaded tree plus the x:Name / Name namescope.</summary>
public sealed class XamlLoadResult
{
    public XamlLoadResult(UIElement root, IReadOnlyDictionary<string, object> names, string? rootClass = null, IReadOnlyList<XamlEventWire>? eventWires = null)
    {
        Root = root;
        Names = names;
        RootClass = rootClass;
        EventWires = eventWires ?? [];
    }

    public UIElement Root { get; }
    public IReadOnlyDictionary<string, object> Names { get; }

    /// <summary>Event attributes in document order (bound by converted-app boot).</summary>
    public IReadOnlyList<XamlEventWire> EventWires { get; }

    /// <summary>
    /// The root's x:Class attribute (converted-app codebehind type name),
    /// null when the root carries none.
    /// </summary>
    public string? RootClass { get; }
}

/// <summary>
/// XAML loading for the browser engine (Application.LoadComponent equivalent).
/// Parses WPF XAML text with System.Xml (in-box and cross-platform) into the
/// managed visual tree: elements, attributes, property-element syntax
/// (Button.Content, TextBlock.Text, Window.Title), attached properties
/// (Grid.Row/Column, Canvas.Left/Top), and x:Name/Name namescopes.
///
/// BAML boundary: compiled MS BAML is produced only by the WindowsDesktop pack
/// (System.Xaml.dll ships there, not in the base Microsoft.NETCore.App shared
/// framework). The engine reads real BAML through <see cref="BamlReader"/>
/// (v1 surface); the desktop Baml2006Reader itself stays Windows-only.
/// Ship loose .xaml or real compiled .baml in the WASM bundle.
/// </summary>
public static partial class XamlLoader
{
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static XamlLoadResult Load(string xaml, IReadOnlyList<FrameworkElement>? templateScope = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        using var reader = XmlReader.Create(new StringReader(xaml), ReaderSettings);
        var lineInfo = (IXmlLineInfo)reader;
        MoveToContentElement(reader, lineInfo);
        var names = new Dictionary<string, object>(StringComparer.Ordinal);
        var pending = new List<PendingStatic>();
        var scope = templateScope is null ? new List<FrameworkElement>() : new List<FrameworkElement>(templateScope);
        // x:Class is accepted-but-ignored by attribute handling; capture it
        // here so converted-app boot can bind the root to its codebehind type.
        var rootClass = reader.GetAttribute("Class", XamlNamespace);
        var wires = new List<XamlEventWire>();
        var visuals = new List<PendingVisual>();
        var root = ReadElement(reader, lineInfo, names, parent: null, pending, scope, wires, visuals);
        // The root never attaches: flush its deferred writes with the full tree linked.
        FlushPending(root, pending, lineInfo, scope, wires);
        if (pending.Count > 0)
            throw Fail(lineInfo, "Internal error: unattached elements kept deferred resources.");
        foreach (var visual in visuals)
        {
            if (!names.TryGetValue(visual.ElementName, out var target) || target is not Visual visualTarget)
                throw Fail(lineInfo, $"VisualBrush Visual ElementName '{visual.ElementName}' was not found in the namescope.");
            visual.Brush.Visual = visualTarget;
        }
        AttachLiveTriggers(root);
        // XAML attributes land after the implicit style applied its template:
        // every inflated template re-resolves once the tree links.
        TemplateApplier.RefreshLoadedTree(root);
        SkipWhitespace(reader);
        if (!reader.EOF)
            throw Fail(lineInfo, "Unexpected trailing content after the root element.");
        return new XamlLoadResult(root, names, string.IsNullOrEmpty(rootClass) ? null : rootClass, wires);
    }

    /// <summary>
    /// Application.LoadComponent equivalent: loose XAML text streams, or real
    /// compiled BAML via <see cref="BamlReader"/> (v1: Window/Grid/TextBlock
    /// content, see BamlReader for the exact supported surface).
    /// </summary>
    public static T LoadComponent<T>(Stream stream) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var probe = new MemoryStream();
        stream.CopyTo(probe);
        var bytes = probe.ToArray();
        if (IsBaml(bytes))
        {
            var root = BamlReader.LoadRoot(bytes);
            if (root is not T typed)
                throw new XamlLoadException($"Root element is {root.GetType().Name}, not {typeof(T).Name}.");
            return typed;
        }

        // Loose XAML files commonly carry a UTF-8 BOM (U+FEFF), which
        // TrimStart leaves behind: strip it before sniffing.
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').TrimStart();
        if (!text.StartsWith("<", StringComparison.Ordinal))
        {
            throw new XamlLoadException(
                "Not XAML text: this stream is neither loose XAML nor MS BAML. " +
                "System.Xaml/Baml2006Reader ship only in the Windows-only WindowsDesktop pack; " +
                "ship loose .xaml or real compiled .baml in the WASM bundle instead.");
        }

        var result = Load(text);
        if (result.Root is not T typed2)
            throw new XamlLoadException($"Root element is {result.Root.GetType().Name}, not {typeof(T).Name}.");
        return typed2;
    }

    /// <summary>MS-BAML signature: I32 length + UTF-16 "MSBAML".</summary>
    internal static bool IsBaml(byte[] bytes) =>
        bytes.Length >= 16 &&
        bytes[0] == 0x0C && bytes[1] == 0x00 && bytes[2] == 0x00 && bytes[3] == 0x00 &&
        bytes[4] == 0x4D && bytes[5] == 0x00 && bytes[6] == 0x53 && bytes[7] == 0x00 &&
        bytes[8] == 0x42 && bytes[9] == 0x00 && bytes[10] == 0x41 && bytes[11] == 0x00 &&
        bytes[12] == 0x4D && bytes[13] == 0x00 && bytes[14] == 0x4C && bytes[15] == 0x00;

    public static string DescribeSupportedSurface() =>
        "v15: Window, Grid (+Row/ColumnDefinitions with pixel/star/Auto, " +
        "Grid.Row/Column/RowSpan/ColumnSpan and Row=/Column= shorthand), " +
        "StackPanel (vertical), Canvas (+Canvas.Left/Top), " +
        "DockPanel (edge docking in child order + LastChildFill, DockPanel.Dock attached), " +
        "Resources (ResourceDictionary + MergedDictionaries, {StaticResource} attribute and property-element " +
        "forms with deferred attach-time resolution, Window.Resources plus Application.Resources (brush/geometry/gradient " +
        "entries with lexical {StaticResource} refs), Application pack store, pack:// and " +
        "relative URIs; inline CLR resource objects and literal implicit Style setters; no DynamicResource), " +
        "Window dialogs (modal ShowDialog/CloseDialog via manager+host: input/focus/Tab gated to the dialog, " +
        "DialogResult/Closed/IsVisible, hidden windows skip render+routing; no auto-close on result set), " +
        "Border (Background/BorderBrush/Thickness/CornerRadius/Padding), Label, Button (Content/Background/Foreground/FontSize/x:Name), " +
        "TextBlock (Text/Foreground/FontSize/VerticalAlignment/text-level HorizontalAlignment), " +
        "TextBox (single-line editor: Text/FontSize/Foreground, caret, keyboard editing), " +
        "ScrollViewer (vertical: wheel/thumb-drag/keyboard, VerticalOffset, ScrollChanged), " +
        "ListBox (ListBoxItem rows, single selection: SelectedIndex/SelectedItem/SelectionChanged, " +
        "press-to-select, arrow/Home/End keys, pending selection from markup, " +
        "ItemsSource binding with generated rows/live sync/guards), " +
        "TreeView (TreeViewItem nodes, expand/collapse, reference selection: SelectedItem/SelectionChanged, " +
        "chevron toggle, arrows/Home/End/Left/Right keys, no SelectedIndex like real WPF), " +
        "DataGrid (DataGridTextColumn columns with Header/BindingPath/Width, plain-CLR rows, " +
        "single row selection: SelectedIndex/SelectedItem/SelectionChanged, INotifyCollectionChanged tracking, " +
        "press-to-select, arrow/Home/End keys; no cell editing/sorting/virtualization), " +
        "ItemsControl (Items/ItemsSource/DisplayMemberPath/AlternationCount, Label containers, ItemTemplateSelector, " +
        "ItemContainerGenerator, ItemsControlFromItemContainer), " +
        "Selector (SelectedIndex/SelectedItem/SelectedValue/SelectedValuePath/SelectionMode, " +
        "IsSynchronizedWithCurrentItem, typed SelectionChanged, CollectionView currency), " +
        "ComboBox (ComboBoxItem rows with Content/IsSelected, header + drop-down panel, press/keyboard, " +
        "Text/IsEditable/IsDropDownOpen/MaxDropDownHeight), " +
        "ListView (ListViewItem rows, Selector selection), " +
        "Calendar (month/year/decade grids: SelectedDate/DisplayDate/DisplayDateStart/DisplayDateEnd/DisplayMode/SelectionMode/IsTodayHighlighted/FirstDayOfWeek, " +
        "click/keyboard selection with blackout/range guards, SelectedDateChanged/SelectedDatesChanged/DisplayDateChanged/DisplayModeChanged/SelectionModeChanged; " +
        "invariant month/day names, no templates/drag/time parts), " +
        "DatePicker (text entry with Enter/lost-focus commit, drop-down Calendar popup: SelectedDate/DisplayDate/DisplayDateStart/DisplayDateEnd/FirstDayOfWeek/IsDropDownOpen/IsTodayHighlighted/Text, " +
        "SelectedDateChanged/CalendarOpened/CalendarClosed/DateValidationError; no SelectedDateFormat/watermark/template parts), " +
        "RichTextBox (FlowDocument editor: typing/caret/selection/Enter-split/Backspace/Delete/arrows/Home/End, TextChanged; no TextPointer/TextRange/Selection/undo/scroll-virtualization), " +
        "StackPanel Orientation=Horizontal, " +
        "Window chrome (ResizeMode/WindowStyle/WindowState/WindowStartupLocation/ShowInTaskbar/Topmost parsed), " +
        "Menu (MenuItem bar with dropdown submenus: press toggles, hover highlights/switches, " +
        "release-inside invokes, arrows/Home/End/Enter/Escape keyboard nav skipping disabled entries, " +
        "Click/Command/CommandParameter with CanExecute greying, IsEnabled gating, focus-loss close; " +
        "Header/IsEnabled/Foreground/FontSize; no separators/check-state/icons/light-dismiss-outside), " +
        "Width/Height/HorizontalAlignment/VerticalAlignment/Margin on all elements (VerticalAlignment " +
        "honored by text rendering; panels keep Top), " +
        "#AARRGGBB/#RRGGBB/named colors, property-element content, namescopes. " +
        "BAML v1: real compiled MS BAML loads through LoadComponent (Window/Grid/TextBlock/TextBox/ScrollViewer/ListBox/ListBoxItem/TreeView/TreeViewItem/Menu/MenuItem/DockPanel/DataGrid/DataGridTextColumn, " +
        "Title/Icon/Height/Width/HorizontalAlignment/VerticalAlignment/TextBox.Text/TextBlock.Text/MenuItem.Header/IsEnabled/DockPanel.Dock/LastChildFill, " +
        "String/Length/Enum/ImageSource/Boolean converters; Icon parsed but not rendered; ConnectionId/Name are namescope-only). " +
        "Binding v1: DataContext inheritance, {Binding [Path][, Mode=OneWay/OneTime/TwoWay]} " +
        "on plain CLR properties, INotifyPropertyChanged push, explicit Source={StaticResource}, property-element Binding, target-to-source UpdateSource, " +
        "ElementName (attach-time FindName), UpdateSourceTrigger (Default/PropertyChanged/LostFocus/Explicit), " +
        "IValueConverter/DoNothing, BindingBase FallbackValue/TargetNullValue/StringFormat. " +
        "ICommand v1: RelayCommand, Button.Command/CommandParameter, CanExecute gates execution " +
        "and greys the button, IsEnabled/IsEffectivelyEnabled honored by focus and input. " +
        "2D media (Geometry mini-language, shape geometries, CombinedGeometry union paint, Drawing/DrawingGroup/ " +
        "GeometryDrawing, TileBrush/DrawingBrush/VisualBrush, gradients; Path/Rectangle shapes on DrawGeometry ops, " +
        "Viewbox fitter, ContentPresenter, Span/Run/LineBreak inlines, NavigationWindow page host), " +
        "ControlTemplate retention (visual root, namescope, deferred TemplateBindings, Trigger/EventTrigger entries; " +
        "LoadContent/ApplyTemplate inflate per control with TemplateBinding/trigger application; " +
        "EventTrigger entries stay a boundary), live FrameworkElement.Triggers (EventTrigger subscribes matching CLR events " +
        "and runs BeginStoryboard storyboards; property triggers retained), " +
        "unsupported event/CLR shapes remain precise-error boundaries. " +
        "Styling v1: Control/UserControl/TextBoxBase, real Style/Setter/EventSetter (implicit, explicit, " +
        "property-element, {StaticResource}, Template Setter.Value; BasedOn and style triggers precise), WPF-typed Background/Foreground/ " +
        "BorderBrush/FontFamily/FontSize/FontWeight/FontStyle with Rgba/CSS sync, CornerRadius struct, " +
        "RoutedEvent/AddHandler/RemoveHandler/RaiseEvent (Tunnel/Bubble/Direct), TextBox selection + IsReadOnly, " +
        "ThemeMode/FontFamily/FontStyle/FontWeight/FontStyles/FontWeights/SystemColors core, Button double-click.";

    private static HorizontalAlignment ParseHAlign(string value, IXmlLineInfo lineInfo) =>
        value switch
        {
            "Left" => HorizontalAlignment.Left,
            "Center" => HorizontalAlignment.Center,
            "Right" => HorizontalAlignment.Right,
            "Stretch" => HorizontalAlignment.Stretch,
            _ => throw Fail(lineInfo, $"'{value}' is not a HorizontalAlignment (Left/Center/Right/Stretch)."),
        };

    private static VerticalAlignment ParseVAlign(string value, IXmlLineInfo lineInfo) =>
        value switch
        {
            "Top" => VerticalAlignment.Top,
            "Center" => VerticalAlignment.Center,
            "Bottom" => VerticalAlignment.Bottom,
            "Stretch" => VerticalAlignment.Stretch,
            _ => throw Fail(lineInfo, $"'{value}' is not a VerticalAlignment (Top/Center/Bottom/Stretch)."),
        };

    private static Thickness ParseThickness(string value, IXmlLineInfo lineInfo)
    {
        try { return Thickness.Parse(value); }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException or OverflowException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    private static CornerRadius ParseCornerRadiusEntry(string value, IXmlLineInfo lineInfo)
    {
        try { return CornerRadius.Parse(value); }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException or OverflowException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    private static string FontSizeToCss(string value, IXmlLineInfo lineInfo)
    {
        var d = ParseDouble(value, lineInfo);
        return FormattableString.Invariant($"{d}px sans-serif");
    }

    /// <summary>Parses a brush literal (hex or named color) into a solid brush.</summary>
    private static SolidColorBrush ParseBrush(string value, IXmlLineInfo lineInfo)
    {
        try { return new SolidColorBrush(StyleValueConverter.ColorFromRgba(ParseColorCore(value))); }
        catch (ArgumentException ex) { throw Fail(lineInfo, ex.Message); }
    }

    /// <summary>Parses path mini-language data (Path.Data and GeometryDrawing.Geometry).</summary>
    /// <summary>
    /// Applies Shape-family attributes (Fill/Stroke/StrokeThickness/Stretch,
    /// stroke caps/join/miter/dash, Line coordinates, Polyline points).
    /// Returns false for attributes owned by narrower slots (Path.Data,
    /// Rectangle radii). Shared slots below stay as the fallback for the
    /// same values; behavior and errors are identical either way.
    /// </summary>
    private static bool TryApplyShapeAttribute(Shape shape, string local, string value, IXmlLineInfo lineInfo)
    {
        switch (local)
        {
        case "Fill":
            shape.Fill = ParseBrush(value, lineInfo);
            return true;
        case "Stroke":
            shape.Stroke = ParseBrush(value, lineInfo);
            return true;
        case "StrokeThickness":
            shape.StrokeThickness = ParseDouble(value, lineInfo);
            return true;
        case "Stretch":
            if (!Enum.TryParse<Stretch>(value, ignoreCase: true, out var stretch))
                throw Fail(lineInfo, "'" + value + "' is not a Stretch (None/Fill/Uniform/UniformToFill).");
            shape.Stretch = stretch;
            return true;
        case "StrokeStartLineCap":
            shape.StrokeStartLineCap = ParsePenLineCap(value, lineInfo);
            return true;
        case "StrokeEndLineCap":
            shape.StrokeEndLineCap = ParsePenLineCap(value, lineInfo);
            return true;
        case "StrokeDashCap":
            shape.StrokeDashCap = ParsePenLineCap(value, lineInfo);
            return true;
        case "StrokeLineJoin":
            if (!Enum.TryParse<PenLineJoin>(value, ignoreCase: true, out var join))
                throw Fail(lineInfo, "'" + value + "' is not a StrokeLineJoin (Miter/Bevel/Round).");
            shape.StrokeLineJoin = join;
            return true;
        case "StrokeMiterLimit":
            shape.StrokeMiterLimit = ParseDouble(value, lineInfo);
            return true;
        case "StrokeDashOffset":
            shape.StrokeDashOffset = ParseDouble(value, lineInfo);
            return true;
        case "StrokeDashArray":
            try { shape.StrokeDashArray = DoubleCollection.Parse(value); }
            catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            return true;
        case "X1" when shape is Line line:
            line.X1 = ParseDouble(value, lineInfo);
            return true;
        case "Y1" when shape is Line line:
            line.Y1 = ParseDouble(value, lineInfo);
            return true;
        case "X2" when shape is Line line:
            line.X2 = ParseDouble(value, lineInfo);
            return true;
        case "Y2" when shape is Line line:
            line.Y2 = ParseDouble(value, lineInfo);
            return true;
        case "Points" when shape is Polyline polyline:
            try { polyline.Points = PointCollection.Parse(value); }
            catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            return true;
        case "FillRule" when shape is Polyline polyline:
            if (!Enum.TryParse<FillRule>(value, ignoreCase: true, out var rule))
                throw Fail(lineInfo, "'" + value + "' is not a FillRule (EvenOdd/Nonzero).");
            polyline.FillRule = rule;
            return true;
        default:
            return false;
        }
    }

    private static PenLineCap ParsePenLineCap(string value, IXmlLineInfo lineInfo)
    {
        if (!Enum.TryParse<PenLineCap>(value, ignoreCase: true, out var cap))
            throw Fail(lineInfo, "'" + value + "' is not a PenLineCap (Flat/Square/Round/Triangle).");
        return cap;
    }

    internal static Geometry ParseGeometry(string value, IXmlLineInfo lineInfo)
    {
        try { return Geometry.Parse(value); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    private static FontWeight ParseFontWeight(string value, IXmlLineInfo lineInfo)
    {
        try { return StyleValueConverter.ParseFontWeight(value); }
        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
    }

    private static FontStyle ParseFontStyle(string value, IXmlLineInfo lineInfo)
    {
        try { return StyleValueConverter.ParseFontStyle(value); }
        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
    }

    /// <summary>
    /// A StaticResource write deferred until the element is attached: attribute
    /// values resolve before the Parent link exists, so ancestor lookup would
    /// spuriously fail. Flushing at attach time keeps document order (same-scope
    /// forward references still fail, like WPF).
    /// </summary>
    private sealed class PendingStatic
    {
        public PendingStatic(UIElement element, string? attribute, string? property, string key)
        {
            Element = element;
            Attribute = attribute;
            Property = property;
            Key = key;
        }

        public UIElement Element { get; }
        public string? Attribute { get; }
        public string? Property { get; }
        public string Key { get; }
        public Binding? Binding { get; init; }

        /// <summary>Converter-only binding (Key is the converter key; the source is left alone).</summary>
        public bool KeepSource { get; init; }

        /// <summary>DynamicResource entry (resolves-or-skips plus subscribes, never fails on missing).</summary>
        public bool IsDynamic { get; init; }
    }

    /// <summary>Deferred visual binding (VisualBrush.Visual ElementName, resolved after names complete).</summary>
    internal sealed class PendingVisual(VisualBrush Brush, string ElementName)
    {
        public VisualBrush Brush { get; } = Brush;
        public string ElementName { get; } = ElementName;
    }

    private static void FlushPending(UIElement element, List<PendingStatic> pending, IXmlLineInfo lineInfo, List<FrameworkElement> scope, List<XamlEventWire> wires)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (!ReferenceEquals(p.Element, element)) continue;
            pending.RemoveAt(i);
            if (!TryFindInScope(element, p.Key, scope, out var res) || res is null)
            {
                // DynamicResource tolerates absent keys (WPF defers them) and
                // refreshes when the key appears; StaticResource fails, like WPF.
                if (!p.IsDynamic || element is not FrameworkElement dynMissing)
                    throw Fail(lineInfo, $"StaticResource '{p.Key}' was not found in element, ancestor or application resources.");
                // Theme keys resolve even without an explicit merge (the engine
                // carries the Fluent pack at application scope); anything else
                // stays deferred until a later refresh.
                if (Application.Current?.TryFindResource(p.Key, out res) == true && res is not null)
                    ApplyResolvedResource(dynMissing, p, res, lineInfo, wires, pending, scope);
                SubscribeDynamicRefresh(dynMissing, p, lineInfo, wires);
                continue;
            }
            if (p.IsDynamic && element is FrameworkElement dynamicTarget)
            {
                ApplyResolvedResource(dynamicTarget, p, res, lineInfo, wires, pending, scope);
                SubscribeDynamicRefresh(dynamicTarget, p, lineInfo, wires);
                continue;
            }
            ApplyResolvedResource(element, p, res, lineInfo, wires, pending, scope);
        }
    }

    /// <summary>
    /// Assigns a resolved resource object directly when the target property
    /// accepts its runtime type (geometry, panels, templates beyond the
    /// fast paths below); otherwise the stringify fallback handles it.
    /// </summary>
    private static bool TryApplyResolvedObject(UIElement element, string name, object res)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var prop = element.GetType().GetProperty(name, flags);
        if (prop is not null && prop.CanWrite && prop.SetMethod?.IsPublic == true
            && prop.PropertyType.IsInstanceOfType(res))
        {
            prop.SetValue(element, res);
            if (res is UIElement visual && element is FrameworkElement fe && visual.Parent is null) fe.AttachVisualChild(visual);
            return true;
        }
        if (element.GetType().GetField(name + "Property",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null)
                is DependencyProperty dp
            && dp.PropertyType.IsInstanceOfType(res))
        {
            element.SetValue(dp, res);
            if (res is UIElement visual2 && element is FrameworkElement fe2 && visual2.Parent is null) fe2.AttachVisualChild(visual2);
            return true;
        }
        return false;
    }

    /// <summary>Applies one resolved resource (shared by static flush and dynamic refresh).</summary>
    private static void ApplyResolvedResource(UIElement element, PendingStatic p, object res, IXmlLineInfo lineInfo, List<XamlEventWire> wires, List<PendingStatic> pending, IReadOnlyList<FrameworkElement>? scope = null)
    {
        if (p.Binding is not null)
        {
            if (element is not FrameworkElement bindingTarget)
                throw Fail(lineInfo, $"Cannot bind '{p.Property ?? p.Attribute}' on <{element.GetType().Name}>.");
            if (p.Binding.DeferredConverterKey is { } converterKey)
                p.Binding.Converter = ResolveConverterKey(converterKey, bindingTarget, lineInfo, scope);
            if (!p.KeepSource)
                p.Binding.Source = res;
            if (p.Property is not null) bindingTarget.SetBinding(p.Property, p.Binding);
            else bindingTarget.SetAttachedBinding(p.Attribute!, p.Binding);
            return;
        }
        if (res is Brush brush && TrySetBrushResource(element, p.Attribute, p.Property, brush))
            return;
        // Theme dictionaries store SolidColorBrush entries as Color (or
        // packed uint, the gallery legacy store); brush targets take them as
        // real brushes, the Setter.CoerceResourceValue rule, so a
        // DynamicResource/StaticResource Background/Foreground lands the
        // upstream brush instead of falling back to text. Non-brush targets
        // keep the stringify path below (TrySetBrushResource fails closed).
        if (res is Color color && TrySetBrushResource(element, p.Attribute, p.Property, new SolidColorBrush(color)))
            return;
        if (res is uint rgba && TrySetBrushResource(element, p.Attribute, p.Property, new SolidColorBrush(StyleValueConverter.ColorFromRgba(rgba))))
            return;
        if (element is FrameworkElement styleTarget && res is Style style &&
            (p.Attribute == "Style" || p.Property == "Style"))
        {
            ApplyStyleResource(styleTarget, style, lineInfo, wires, scope);
            return;
        }
        if (element is Image imgTarget && res is ImageSource image &&
            (p.Attribute == "Source" || p.Property == "Source"))
        {
            imgTarget.Source = image;
            return;
        }
        if (element is ItemsControl itemsTarget && res is ItemsPanelTemplate panel &&
            (p.Attribute == "ItemsPanel" || p.Property == "ItemsPanel"))
        {
            itemsTarget.ItemsPanel = panel;
            return;
        }
        if (element is ItemsControl itemTarget && res is DataTemplate data &&
            (p.Attribute == "ItemTemplate" || p.Property == "ItemTemplate"))
        {
            itemTarget.ItemTemplate = data;
            return;
        }
        // Retained Blend layout knob (matches PanelElement.FluidMoveBehavior):
        // the resolved marker (BehaviorXY200, see Slice111HandyPanels) lands
        // in the object-typed DP without invoking converted callbacks, so the
        // animation stays a declared boundary while the panel itself loads.
        if ((p.Attribute == "PanelElement.FluidMoveBehavior" || p.Property == "PanelElement.FluidMoveBehavior") &&
            res is not null)
        {
            element.SetValue(PanelElement.FluidMoveBehaviorProperty, res);
            return;
        }
        string text;
        if (TryApplyResolvedObject(element, p.Attribute ?? p.Property!, res)) return;
        if (TryApplyConvertedAttachedObject(element, p.Attribute ?? p.Property!, res, lineInfo)) return;
        try { text = Application.ResourceToString(res, p.Key); }
        catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
        if (p.Attribute is not null) ApplyPlainAttribute(element, p.Attribute, text, lineInfo);
        else SetPropertyElement(element, p.Property!, text, lineInfo, pending);
    }

    /// <summary>Subscribes a DynamicResource entry for resource/theme refresh (re-applies on change).</summary>
    private static void SubscribeDynamicRefresh(FrameworkElement target, PendingStatic p, IXmlLineInfo lineInfo, List<XamlEventWire> wires)
    {
        var owner = "dynamic:" + (p.Attribute ?? ("prop:" + p.Property));
        Application.DynamicResourceManager.SubscribeProperty(target, owner, p.Key,
            (refreshed, value) =>
            {
                if (value is null) return;
                try { ApplyResolvedResource(refreshed, p, value, lineInfo, wires, new List<PendingStatic>()); }
                catch
                {
                    // Refresh keeps old values when a key disappears or
                    // mistypes mid-life; load time stays loud instead.
                }
            });
    }

    /// <summary>
    /// Scoped lookup for deferred writes: the element, then the lexical
    /// ancestor scopes (innermost first), then application resources. The
    /// loader threads scopes explicitly because intermediate Parent links do
    /// not exist yet at attach time; reading whatever each scope currently
    /// holds preserves WPF document order (same-scope forward references
    /// still fail).
    /// </summary>
    private static bool TryFindInScope(UIElement element, object key, List<FrameworkElement> scope, out object? value)
    {
        if (element is FrameworkElement fer && fer.Resources.TryGetValue(key, out value)) return true;
        for (var i = scope.Count - 1; i >= 0; i--)
            if (scope[i].Resources.TryGetValue(key, out value)) return true;
        var app = Application.Current;
        // Application scope includes the framework-provided Fluent theme
        // keys (converted Fluent-era pages reference them from StaticResource
        // without merging in test harnesses, like the real merged pack).
        if (app is not null && app.TryFindResource(key, out value) && value is not null) return true;
        value = null;
        return false;
    }

    private static UIElement ReadElement(XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, UIElement? parent, List<PendingStatic> pending, List<FrameworkElement> scope, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        var name = reader.LocalName;
        var element = CreateElement(name, reader.NamespaceURI, lineInfo);
        // Converted default styles (Themes/Generic.xaml by DefaultStyleKey):
        // theme first, then the lexical implicit style, like WPF precedence.
        if (element is FrameworkElement styled)
            ThemeDictionaries.ApplyDefaultStyle(styled);
        ApplyImplicitStyle(element, scope, lineInfo, wires);
        var textContent = new StringBuilder();

        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns") continue;
                if (reader.Name == "xmlns") continue;
                if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue; // xml:space etc.
                ApplyAttribute(element, reader, lineInfo, names, pending, wires);
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
        {
            reader.Read();
            FinishTextContent(element, textContent.ToString(), lineInfo);
            return element;
        }

        reader.Read(); // past start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, $"Unterminated element <{name}>.");
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    ReadChildNode(element, reader, lineInfo, names, pending, scope, wires, visuals);
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    textContent.Append(reader.Value);
                    reader.Read();
                    break;
                default:
                    reader.Read();
                    break;
            }
        }

        reader.Read(); // past end tag
        FinishTextContent(element, textContent.ToString(), lineInfo);
        return element;
    }

    private static void ReadChildNode(UIElement element, XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, List<PendingStatic> pending, List<FrameworkElement> scope, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        // Property-element syntax: <Button.Content>text</Button.Content>.
        if (reader.LocalName.Contains('.'))
        {
            var parts = reader.LocalName.Split('.', 2);
            if (string.Equals(parts[0], "Interaction", StringComparison.Ordinal) && parts[1] == "Triggers")
            {
                ReadInteractionTriggers(element, reader, lineInfo);
                return;
            }
            if (string.Equals(parts[0], "AutomationProperties", StringComparison.Ordinal))
            {
                ReadAutomationPropertyElement(element, parts[1], reader, lineInfo);
                return;
            }
            if (!string.Equals(parts[0], element.GetType().Name, StringComparison.Ordinal))
            {
                // UIElement-owned property elements (RenderTransform and
                // friends) are valid on every element in real WPF; the
                // member branches below handle the supported ones and fail
                // precisely on the rest. Anything else keeps the owner check.
                if (!(string.Equals(parts[0], "UIElement", StringComparison.Ordinal) && element is UIElement))
                {
                    // Attached-owner binding elements (TextFieldAssist thickness
                    // overrides on templated parts): bind before the precise
                    // owner error; anything else keeps it.
                    if (element is FrameworkElement attachedTarget
                        && TryReadAttachedPriorityBinding(attachedTarget, parts[0], parts[1], reader, lineInfo))
                        return;
                    throw Fail(lineInfo, $"<{reader.LocalName}> does not belong to <{element.GetType().Name}>.");
                }
            }
            if (element is Grid grid && parts[1] is "RowDefinitions" or "ColumnDefinitions")
            {
                ReadGridDefinitions(reader, lineInfo, grid, parts[1] == "RowDefinitions", names);
                return;
            }
            if (element is Window taskbarWindow && parts[1] == "TaskbarItemInfo")
            {
                ReadWindowTaskbarItemInfo(reader, lineInfo, taskbarWindow, names);
                return;
            }
            if (element is DataGrid dataGrid && parts[1] == "Columns")
            {
                ReadDataGridColumns(reader, lineInfo, dataGrid);
                return;
            }
            if (parts[1] == "InputBindings" && element is UIElement inputHost)
            {
                ReadElementInputBindings(reader, lineInfo, inputHost);
                return;
            }
            if (parts[1] == "CommandBindings" && element is UIElement commandHost)
            {
                ReadElementCommandBindings(reader, lineInfo, commandHost, wires);
                return;
            }
            if (element is ItemsControl itemsHost && parts[1] is "ItemTemplate" or "ItemsPanel")
            {
                if (parts[1] == "ItemTemplate") ReadElementItemTemplate(reader, lineInfo, itemsHost, scope);
                else ReadElementItemsPanel(reader, lineInfo, itemsHost);
                return;
            }
            if (element is ItemsControl itemsStyle && parts[1] == "ItemContainerStyle")
            {
                ReadElementItemContainerStyle(reader, lineInfo, itemsStyle);
                return;
            }
            if (element is ListBox legacyList && parts[1] is "ItemTemplate" or "ItemsPanel")
            {
                if (parts[1] == "ItemTemplate") ReadListBoxItemTemplate(reader, lineInfo, legacyList, scope);
                else ReadLegacyListBoxItemsPanel(reader, lineInfo, legacyList);
                return;
            }
            if (element is TabControl tabs && parts[1] is "ContentTemplate" or "SelectedContentTemplate")
            {
                ReadTabContentTemplate(reader, lineInfo, tabs, parts[1], scope);
                return;
            }
            if (element is ListBox listGroupStyle && parts[1] == "GroupStyle")
            {
                ReadListBoxGroupStyle(reader, lineInfo, listGroupStyle);
                return;
            }
            if (element is Expander expanderSlot && parts[1] is "Header" or "Content")
            {
                ReadExpanderSlot(reader, lineInfo, expanderSlot, parts[1], names, pending, scope, wires, visuals);
                return;
            }
            if (element is Viewport3D viewportProp)
            {
                ReadViewportChild(viewportProp, reader, lineInfo, names, wires, visuals);
                return;
            }
            if (parts[1] == "Resources" && element is FrameworkElement feTarget)
            {
                ReadElementResources(reader, lineInfo, feTarget, wires);
                return;
            }
            if (parts[1] == "Style" && element is FrameworkElement feStyle)
            {
                ReadElementStyle(reader, lineInfo, feStyle, wires, scope);
                return;
            }
            if (parts[1] == "Source" && element is Image imgSource)
            {
                ReadImageSource(reader, lineInfo, imgSource);
                return;
            }
            if (parts[1] == "Triggers" && element is FrameworkElement feTriggers)
            {
                ReadElementTriggers(reader, lineInfo, feTriggers, wires);
                return;
            }
            if (parts[1] == "Document" && element is RichTextBox richDocument)
            {
                reader.Read(); // past the property start tag
                SkipWhitespace(reader);
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "FlowDocument")
                    throw Fail(lineInfo, "<RichTextBox.Document> takes a single <FlowDocument>.");
                var nestedDocument = ReadElement(reader, lineInfo, names, element, pending, scope, wires, visuals);
                if (nestedDocument is not FlowDocument flowDocument)
                    throw Fail(lineInfo, "<RichTextBox.Document> takes a single <FlowDocument>.");
                richDocument.Document = flowDocument;
                FlushPending(nestedDocument, pending, lineInfo, scope, wires);
                SkipWhitespace(reader);
                if (reader.NodeType != XmlNodeType.EndElement)
                    throw Fail(lineInfo, "Malformed <RichTextBox.Document>.");
                reader.Read(); // past the property end tag
                return;
            }
            if (parts[1] == "RenderTransform" && element is FrameworkElement feTransform)
            {
                ReadElementRenderTransform(reader, lineInfo, feTransform);
                return;
            }
            if (TryReadMediaProperty(element, parts[1], reader, lineInfo, pending))
                return;
            if (TryReadEffectProperty(element, parts[1], reader, lineInfo))
                return;
            if (reader.IsEmptyElement)
            {
                reader.Read();
                SetPropertyElement(element, parts[1], string.Empty, lineInfo, pending);
                return;
            }
            reader.Read(); // past the property start tag
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName is "Binding" or "PriorityBinding" && element is FrameworkElement bindingTarget)
                {
                    if (reader.LocalName == "PriorityBinding")
                    {
                        ReadPriorityBindingProperty(reader, lineInfo, bindingTarget, parts[1]);
                        return;
                    }
                    ReadBindingProperty(reader, lineInfo, bindingTarget, parts[1], pending);
                    return;
                }
                                if (parts[1] == "DataContext" && element is FrameworkElement dataContextTarget
                    && TryCreateDataContextValue(reader, lineInfo, out var dataContextValue))
                {
                    dataContextTarget.DataContext = dataContextValue;
                    SkipWhitespace(reader);
                    if (reader.NodeType != XmlNodeType.EndElement)
                        throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.DataContext>.");
                    reader.Read(); // past the property end tag
                    return;
                }
                if (reader.LocalName is "DataTemplate" or "HierarchicalDataTemplate")
                {
                    var dataTemplate = ReadDataTemplateElement(reader, lineInfo, scope);
                    if (TrySetPropertyElementValue(element, parts[1], dataTemplate, lineInfo))
                    {
                        SkipWhitespace(reader);
                        if (reader.NodeType != XmlNodeType.EndElement)
                            throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.{parts[1]}>.");
                        reader.Read(); // past the property end tag
                        return;
                    }
                    throw Fail(lineInfo, $"<DataTemplate> nested in <{element.GetType().Name}.{parts[1]}> needs a settable DataTemplate slot.");
                }
                var nestedName = reader.LocalName;
                var nested = ReadElement(reader, lineInfo, names, element, pending, scope, wires, visuals);
                if (TrySetPropertyElementValue(element, parts[1], nested, lineInfo))
                {
                    // Property-element values attach without AddContentChild,
                    // so their own deferred resources (gallery MenuPage icon
                    // headers carry StaticResource FontFamily) flush here,
                    // mirroring the attach-time flush of direct children.
                    FlushPending(nested, pending, lineInfo, scope, wires);
                    SkipWhitespace(reader);
                    if (reader.NodeType != XmlNodeType.EndElement)
                        throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.{parts[1]}>.");
                    reader.Read(); // past the property end tag
                    return;
                }
                throw Fail(lineInfo, $"<{nestedName}> nested in <{element.GetType().Name}.{parts[1]}> is not supported (data binding and resources are a later milestone).");
            }
            var value = string.Empty;
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                value = reader.Value;
                reader.Read();
                SkipWhitespace(reader);
            }
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.{parts[1]}>.");
            reader.Read(); // past the property end tag
            SetPropertyElement(element, parts[1], value, lineInfo, pending);
            return;
        }

        if (element is DataGrid && reader.LocalName == "DataGridTextColumn")
            throw Fail(lineInfo, "<DataGrid> takes column definitions in <DataGrid.Columns>; rows are data, set in code.");
        if (element is Viewport3D viewport)
        {
            ReadViewportChild(viewport, reader, lineInfo, names, wires, visuals);
            return;
        }
        var childScope = scope;
        if (element is FrameworkElement fer)
        {
            childScope = new List<FrameworkElement>(scope) { fer };
        }
        var child = ReadElement(reader, lineInfo, names, element, pending, childScope, wires, visuals);
        AddContentChild(element, child, lineInfo, pending, childScope, wires);
    }

    /// <summary>Reads RowDefinition/ColumnDefinition children into the grid.</summary>
    private static void ReadGridDefinitions(XmlReader reader, IXmlLineInfo lineInfo, Grid grid, bool isRow, Dictionary<string, object> names)
    {
        var want = isRow ? "RowDefinition" : "ColumnDefinition";
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <Grid.RowDefinitions>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated grid definitions.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != want)
                throw Fail(lineInfo, $"Expected <{want}> inside grid definitions.");
            object definition = isRow ? new RowDefinition() : new ColumnDefinition();
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns") continue;
                    if (reader.Name == "xmlns") continue;
                    var attr = isRow ? "Height" : "Width";
                    var minAttr = isRow ? "MinHeight" : "MinWidth";
                    var maxAttr = isRow ? "MaxHeight" : "MaxWidth";
                    if (reader.Prefix == "x" && reader.LocalName == "Name")
                    {
                        if (names.ContainsKey(reader.Value)) throw Fail(lineInfo, $"Duplicate x:Name '{reader.Value}'.");
                        names[reader.Value] = definition;
                    }
                    else if (reader.LocalName == attr)
                    {
                        try { SetDefinitionLength(definition, GridLength.Parse(reader.Value)); }
                        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
                        {
                            throw Fail(lineInfo, ex.Message);
                        }
                    }
                    else if (reader.LocalName == minAttr)
                        SetDefinitionMin(definition, ParseDouble(reader.Value, lineInfo));
                    else if (reader.LocalName == maxAttr)
                        SetDefinitionMax(definition, ParseDouble(reader.Value, lineInfo));
                    else throw Fail(lineInfo, $"{reader.LocalName} is not valid on <{want}> (only {attr}/{minAttr}/{maxAttr}/x:Name).");
                }
                reader.MoveToElement();
            }
            var wasEmpty = reader.IsEmptyElement;
            if (definition is RowDefinition row) grid.RowDefinitions.Add(row);
            else grid.ColumnDefinitions.Add((ColumnDefinition)definition);
            reader.Read(); // past the definition start tag
            if (!wasEmpty)
            {
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.EOF) throw Fail(lineInfo, $"Unterminated <{want}>.");
                    reader.Read();
                }
                reader.Read(); // past the definition end tag
            }
        }
        reader.Read(); // past </Grid.RowDefinitions>
    }

    /// <summary>
    /// Reads Window.TaskbarItemInfo (ScreenToGif recorder windows): one
    /// TaskbarItemInfo carrying ThumbButtonInfos. Command and ImageSource
    /// bindings stay code-set (converted apps bind them at boot); literal
    /// labels, flags and visibility parse here.
    /// </summary>
    private static void ReadWindowTaskbarItemInfo(XmlReader reader, IXmlLineInfo lineInfo, Window window, Dictionary<string, object> names)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <Window.TaskbarItemInfo>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <Window.TaskbarItemInfo>.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "TaskbarItemInfo")
                throw Fail(lineInfo, $"Expected <TaskbarItemInfo> inside <Window.TaskbarItemInfo> (got <{reader.LocalName}>).");
            window.TaskbarItemInfo = ReadTaskbarItemInfo(reader, lineInfo, names);
        }
        reader.Read(); // past </Window.TaskbarItemInfo>
    }

    private static TaskbarItemInfo ReadTaskbarItemInfo(XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names)
    {
        var info = new TaskbarItemInfo();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns") continue;
                if (reader.Name == "xmlns") continue;
                switch (reader.LocalName)
                {
                    case "Description":
                        info.Description = reader.Value;
                        break;
                    case "ProgressState":
                        if (!Enum.TryParse<TaskbarItemProgressState>(reader.Value, ignoreCase: true, out var state))
                            throw Fail(lineInfo, $"'{reader.Value}' is not a TaskbarItemProgressState (None/Indeterminate/Normal/Error/Paused).");
                        info.ProgressState = state;
                        break;
                    case "ProgressValue":
                        info.ProgressValue = ParseDouble(reader.Value, lineInfo);
                        break;
                    default:
                        throw Fail(lineInfo, $"{reader.LocalName} is not valid on <TaskbarItemInfo> (Description/ProgressState/ProgressValue plus TaskbarItemInfo.ThumbButtonInfos).");
                }
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) { reader.Read(); return info; }
        reader.Read(); // past <TaskbarItemInfo>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <TaskbarItemInfo>.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "TaskbarItemInfo.ThumbButtonInfos")
                throw Fail(lineInfo, $"Expected <TaskbarItemInfo.ThumbButtonInfos> inside <TaskbarItemInfo> (got <{reader.LocalName}>).");
            ReadThumbButtonInfos(reader, lineInfo, info, names);
        }
        reader.Read(); // past </TaskbarItemInfo>
        return info;
    }

    private static void ReadThumbButtonInfos(XmlReader reader, IXmlLineInfo lineInfo, TaskbarItemInfo info, Dictionary<string, object> names)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <TaskbarItemInfo.ThumbButtonInfos>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <TaskbarItemInfo.ThumbButtonInfos>.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ThumbButtonInfoCollection")
                throw Fail(lineInfo, $"Expected <ThumbButtonInfoCollection> inside <TaskbarItemInfo.ThumbButtonInfos> (got <{reader.LocalName}>).");
            if (reader.IsEmptyElement) { reader.Read(); continue; }
            reader.Read(); // past <ThumbButtonInfoCollection>
            while (true)
            {
                SkipWhitespace(reader);
                if (reader.NodeType == XmlNodeType.EndElement) break;
                if (reader.EOF) throw Fail(lineInfo, "Unterminated <ThumbButtonInfoCollection>.");
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ThumbButtonInfo")
                    throw Fail(lineInfo, $"Expected <ThumbButtonInfo> inside <ThumbButtonInfoCollection> (got <{reader.LocalName}>).");
                info.ThumbButtonInfos.Add(ReadThumbButton(reader, lineInfo, names));
            }
            reader.Read(); // past </ThumbButtonInfoCollection>
        }
        reader.Read(); // past </TaskbarItemInfo.ThumbButtonInfos>
    }

    private static ThumbButtonInfo ReadThumbButton(XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names)
    {
        var button = new ThumbButtonInfo();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns") continue;
                if (reader.Name == "xmlns") continue;
                if ((reader.Prefix == "x" && reader.LocalName is "Name" or "Key") || (reader.Prefix == string.Empty && reader.LocalName == "Name"))
                {
                    if (names.ContainsKey(reader.Value)) throw Fail(lineInfo, $"Duplicate x:Name '{reader.Value}'.");
                    names[reader.Value] = button;
                    continue;
                }
                if (reader.Value.StartsWith('{'))
                    throw Fail(lineInfo, $"'{reader.LocalName}' takes a literal here (converted apps bind ThumbButtonInfo commands at boot).");
                switch (reader.LocalName)
                {
                    case "Description":
                        button.Description = reader.Value;
                        break;
                    case "DismissWhenClicked":
                        button.DismissWhenClicked = ParseBool(reader.Value, lineInfo);
                        break;
                    case "IsBackgroundVisible":
                        button.IsBackgroundVisible = ParseBool(reader.Value, lineInfo);
                        break;
                    case "IsEnabled":
                        button.IsEnabled = ParseBool(reader.Value, lineInfo);
                        break;
                    case "IsInteractive":
                        button.IsInteractive = ParseBool(reader.Value, lineInfo);
                        break;
                    case "Visibility":
                        button.Visibility = ParseVisibility(reader.Value, lineInfo);
                        break;
                    default:
                        throw Fail(lineInfo, $"{reader.LocalName} is not valid on <ThumbButtonInfo> (Description/DismissWhenClicked/IsBackgroundVisible/IsEnabled/IsInteractive/Visibility plus ThumbButtonInfo.ImageSource).");
                }
            }
            reader.MoveToElement();
        }
        var wasEmpty = reader.IsEmptyElement;
        reader.Read(); // past the <ThumbButtonInfo> start tag
        if (wasEmpty) return button;
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <ThumbButtonInfo>.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ThumbButtonInfo.ImageSource")
                throw Fail(lineInfo, $"Expected <ThumbButtonInfo.ImageSource> inside <ThumbButtonInfo> (got <{reader.LocalName}>).");
            ReadThumbButtonImageSource(reader, lineInfo, button);
        }
        reader.Read(); // past </ThumbButtonInfo>
        return button;
    }

    private static void ReadThumbButtonImageSource(XmlReader reader, IXmlLineInfo lineInfo, ThumbButtonInfo button)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <ThumbButtonInfo.ImageSource>
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "DrawingImage")
            throw Fail(lineInfo, $"Expected <DrawingImage> inside <ThumbButtonInfo.ImageSource> (got <{reader.LocalName}>).");
        if (reader.HasAttributes)
            throw Fail(lineInfo, "<DrawingImage> takes no attributes here (set Drawing in code).");
        reader.Read(); // past <DrawingImage>
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, "<DrawingImage> takes no children here (set Drawing in code).");
        button.ImageSource = new DrawingImage();
        reader.Read(); // past </ThumbButtonInfo.ImageSource>; the caller loop closes the button
    }

    /// <summary>
    /// .NET Grid shorthand (gallery WhatsNewPage): comma- or space-separated
    /// GridLength tokens ("Auto,Auto,Auto" / "Auto Auto *"); each becomes an
    /// explicit definition like the property-element form.
    /// </summary>
    private static void ParseGridShorthandDefinitions(Grid grid, bool isRow, string value, IXmlLineInfo lineInfo)
    {
        var tokens = value.Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 0)
        throw Fail(lineInfo, $"{value} needs at least one row/column length.");
    foreach (var token in tokens)
    {
        GridLength length;
        try { length = GridLength.Parse(token); }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
        {
            throw Fail(lineInfo, ex.Message);
        }
        if (isRow) grid.RowDefinitions.Add(new RowDefinition { Height = length });
        else grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
    }
    }

    private static void SetDefinitionLength(object definition, GridLength length)
    {
        if (definition is RowDefinition row) row.Height = length;
        else ((ColumnDefinition)definition).Width = length;
    }

    private static void SetDefinitionMin(object definition, double value)
    {
        if (definition is RowDefinition row) row.MinHeight = value;
        else ((ColumnDefinition)definition).MinWidth = value;
    }

    private static void SetDefinitionMax(object definition, double value)
    {
        if (definition is RowDefinition row) row.MaxHeight = value;
        else ((ColumnDefinition)definition).MaxWidth = value;
    }

    /// <summary>Reads DataGridTextColumn children into the grid (mirrors grid definitions).</summary>
    private static void ReadDataGridColumns(XmlReader reader, IXmlLineInfo lineInfo, DataGrid grid)
    {
        const string want = "DataGridTextColumn";
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <DataGrid.Columns>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated data grid columns.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != want)
                throw Fail(lineInfo, $"Expected <{want}> inside data grid columns.");
            var column = new DataGridTextColumn();
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns") continue;
                    if (reader.Name == "xmlns") continue;
                    switch (reader.LocalName)
                    {
                        case "Header": column.Header = reader.Value; break;
                        case "BindingPath": column.BindingPath = reader.Value; break;
                        case "SortMemberPath": column.SortMemberPath = reader.Value; break;
                        case "Binding":
                            { // Real column bindings (the PC Space Map grids bind Name/SizeText/...); the
                                // effective path drives cell text through BindingExpression like BindingPath.
                                var text = reader.Value.Trim();
                                if (!text.StartsWith("{Binding", StringComparison.Ordinal) || !text.EndsWith('}'))
                                    throw Fail(lineInfo, $"'{text}' is not a Binding for <" + want + "> (use Binding=\"{Binding Path}\").");
                                column.Binding = ParseBinding(text["{Binding".Length..^1], text, lineInfo);
                            }
                            break;
                        case "Width":
                            try { column.Width = GridLength.Parse(reader.Value); }
                            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
                            {
                                throw Fail(lineInfo, $"'{reader.Value}' is not a grid length for Width ({ex.Message}).");
                            }
                            break;
                        default: throw Fail(lineInfo, $"'{reader.LocalName}' is not valid on <{want}> (Header/Binding/BindingPath/SortMemberPath/Width).");
                    }
                }
                reader.MoveToElement();
            }
            grid.AddColumn(column);
            var wasEmpty = reader.IsEmptyElement;
            reader.Read(); // past the column start tag
            if (!wasEmpty)
            {
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    if (reader.EOF) throw Fail(lineInfo, $"Unterminated <{want}>.");
                    reader.Read();
                }
                reader.Read(); // past the column end tag
            }
        }
        reader.Read(); // past </DataGrid.Columns>
    }

    /// <summary>
    /// Reads an <c>&lt;X.InputBindings&gt;</c> property element (matches
    /// UIElement.InputBindings): KeyBinding/MouseBinding children land in the
    /// live collection with parsed gestures. Command bindings attach through
    /// the DP transfer and capture the host data context when it lands (input
    /// bindings carry no inheritance context of their own; later view-model
    /// swaps do not re-track).
    /// </summary>
    private static void ReadElementInputBindings(XmlReader reader, IXmlLineInfo lineInfo, UIElement host)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the property start tag
        var live = new List<(InputBinding Target, DependencyProperty Property, Binding Source)>();
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated input bindings.");
            InputBinding binding = reader.LocalName switch
            {
                "KeyBinding" => new KeyBinding(),
                "MouseBinding" => new MouseBinding(),
                _ => throw Fail(lineInfo, $"<{reader.LocalName}> is not an input binding (KeyBinding/MouseBinding)."),
            };
            ReadInputBindingAttributes(binding, reader, lineInfo, live);
            host.InputBindings.Add(binding);
        }
        reader.Read(); // past the property end tag
        if (live.Count != 0 && host is FrameworkElement contextHost)
        {
            void Refresh(object? context)
            {
                if (context is null) return;
                foreach (var (target, property, source) in live)
                {
                    source.Source = context;
                    (BindingOperations.GetBindingExpressionBase(target, property) as DependencyObjectBinding)?.UpdateTarget();
                }
            }
            Refresh(contextHost.DataContext);
            contextHost.DataContextChanged += (_, e) => Refresh(e.NewValue);
        }
    }

    private static void ReadInputBindingAttributes(InputBinding binding, XmlReader reader, IXmlLineInfo lineInfo, List<(InputBinding, DependencyProperty, Binding)> live)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns") continue;
                if (reader.Name == "xmlns") continue;
                switch (reader.LocalName)
                {
                    case "Key" when binding is KeyBinding keyBinding:
                        if (!Enum.TryParse<Key>(reader.Value, ignoreCase: true, out var key))
                            throw Fail(lineInfo, $"'{reader.Value}' is not a Key for <KeyBinding>.");
                        keyBinding.Key = key;
                        break;
                    case "Modifiers" when binding is KeyBinding keyBinding:
                        if (!Enum.TryParse<ModifierKeys>(reader.Value, ignoreCase: true, out var modifiers))
                            throw Fail(lineInfo, $"'{reader.Value}' is not ModifierKeys for <KeyBinding>.");
                        keyBinding.Modifiers = modifiers;
                        break;
                    case "MouseAction" when binding is MouseBinding mouseBinding:
                        if (!Enum.TryParse<MouseAction>(reader.Value, ignoreCase: true, out var action))
                            throw Fail(lineInfo, $"'{reader.Value}' is not a MouseAction for <MouseBinding>.");
                        mouseBinding.MouseAction = action;
                        break;
                    case "Command":
                        ApplyInputBindingCommand(binding, InputBinding.CommandProperty, reader.Value, lineInfo, live);
                        break;
                    case "CommandParameter":
                        ApplyInputBindingCommand(binding, InputBinding.CommandParameterProperty, reader.Value, lineInfo, live);
                        break;
                    default: throw Fail(lineInfo, $"'{reader.LocalName}' is not valid on <{binding.GetType().Name}> (Key/Modifiers/MouseAction/Command/CommandParameter).");
                }
            }
            reader.MoveToElement();
        }
        if (!reader.IsEmptyElement)
            throw Fail(lineInfo, $"<{binding.GetType().Name}> takes attributes only (Key/Modifiers/Command).");
        reader.Read(); // past the binding element
    }

    private static void ApplyInputBindingCommand(InputBinding binding, DependencyProperty property, string raw, IXmlLineInfo lineInfo, List<(InputBinding, DependencyProperty, Binding)> live)
    {
        var text = raw.Trim();
        if (!text.StartsWith("{Binding", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(lineInfo, $"'{raw}' is not a Binding for input binding commands (use Command=\"{{Binding Path}}\").");
        var parsed = ParseBinding(text["{Binding".Length..^1], text, lineInfo);
        BindingOperations.SetBinding(binding, property, parsed);
        if (parsed.Source is null && parsed.ElementName is null && parsed.RelativeSource is null)
            live.Add((binding, property, parsed));
    }

    /// <summary>
    /// Reads an <c>&lt;X.CommandBindings&gt;</c> property element (matches
    /// UIElement.CommandBindings): CommandBinding children land in the live
    /// collection with ApplicationCommands-static commands; Executed-style
    /// handler names ride the shared event-wire list to the converted
    /// codebehind like element handlers.
    /// </summary>
    private static void ReadElementCommandBindings(XmlReader reader, IXmlLineInfo lineInfo, UIElement host, List<XamlEventWire> wires)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the property start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated command bindings.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "CommandBinding")
                throw Fail(lineInfo, $"<{reader.LocalName}> is not a command binding (CommandBinding).");
            var commandBinding = new CommandBinding();
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns") continue;
                    if (reader.Name == "xmlns") continue;
                    switch (reader.LocalName)
                    {
                        case "Command":
                            commandBinding.Command = ResolveApplicationCommand(reader.Value, lineInfo);
                            break;
                        case "Executed":
                            wires.Add(new XamlEventWire(commandBinding, "Executed", reader.Value));
                            break;
                        case "PreviewExecuted":
                            wires.Add(new XamlEventWire(commandBinding, "PreviewExecuted", reader.Value));
                            break;
                        case "CanExecute":
                            wires.Add(new XamlEventWire(commandBinding, "CanExecute", reader.Value));
                            break;
                        case "PreviewCanExecute":
                            wires.Add(new XamlEventWire(commandBinding, "PreviewCanExecute", reader.Value));
                            break;
                        default: throw Fail(lineInfo, $"'{reader.LocalName}' is not valid on <CommandBinding> (Command/Executed/CanExecute).");
                    }
                }
                reader.MoveToElement();
            }
            host.CommandBindings.Add(commandBinding);
            if (!reader.IsEmptyElement)
                throw Fail(lineInfo, "<CommandBinding> takes attributes only (Command/Executed/CanExecute).");
            reader.Read(); // past the binding element
        }
        reader.Read(); // past the property end tag
    }

    private static ICommand ResolveApplicationCommand(string raw, IXmlLineInfo lineInfo)
    {
        var property = typeof(ApplicationCommands).GetProperty(raw,
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.IgnoreCase);
        if (property?.GetValue(null) is ICommand command)
            return command;
        throw Fail(lineInfo, $"'{raw}' is not an ApplicationCommands command (Copy/New/Open/Save/...).");
    }

    /// <summary>
    /// Reads one Viewport3D child: the Camera property-element or a visual
    /// nested directly (ModelVisual3D). Anything else (lights/materials as
    /// direct children, UIElement content) is a precise error.
    /// </summary>
    private static void ReadViewportChild(Viewport3D viewport, XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        if (reader.LocalName.Contains('.'))
        {
            var parts = reader.LocalName.Split('.', 2);
            if (!string.Equals(parts[0], nameof(Viewport3D), StringComparison.Ordinal))
                throw Fail(lineInfo, $"<{reader.LocalName}> does not belong to <Viewport3D>.");
            if (parts[1] != "Camera")
                throw Fail(lineInfo, $"<Viewport3D.{parts[1]}> is not supported (only Camera).");
            if (reader.IsEmptyElement) { reader.Read(); return; }
            reader.Read(); // past the property start tag
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(lineInfo, "<Viewport3D.Camera> needs a camera element (PerspectiveCamera).");
            var camera = ReadModelNode(reader, lineInfo, names, wires, visuals);
            if (camera is not PerspectiveCamera perspective)
                throw Fail(lineInfo, $"<Viewport3D.Camera> needs a PerspectiveCamera, not <{camera.GetType().Name}> (orthographic cameras stay a precise boundary).");
            viewport.Camera = perspective;
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, "Malformed <Viewport3D.Camera>.");
            reader.Read(); // past the property end tag
            return;
        }
        var child = ReadModelNode(reader, lineInfo, names, wires, visuals);
        if (child is not Visual3D visual)
            throw Fail(lineInfo, $"<{child.GetType().Name}> cannot nest directly in <Viewport3D> (use ModelVisual3D).");
        viewport.Children.Add(visual);
    }

    /// <summary>
    /// Reads one 3D scene node (positioned on its start tag) and consumes it:
    /// models, visuals, cameras, lights, materials, transforms and rotations.
    /// Brush-backed content and key-frame animations stay precise boundaries;
    /// everything else 3D parses or fails loudly.
    /// </summary>
    private static object ReadModelNode(XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        var tag = reader.LocalName;
        object node = tag switch
        {
            "ModelVisual3D" => new ModelVisual3D(),
            "PerspectiveCamera" => new PerspectiveCamera(),
            "GeometryModel3D" => new GeometryModel3D(),
            "MeshGeometry3D" => new MeshGeometry3D(),
            "DiffuseMaterial" => new DiffuseMaterial(),
            "AmbientLight" => new AmbientLight(),
            "DirectionalLight" => new DirectionalLight(),
            "Transform3DGroup" => new Transform3DGroup(),
            "RotateTransform3D" => new RotateTransform3D(),
            "ScaleTransform3D" => new ScaleTransform3D(),
            "TranslateTransform3D" => new TranslateTransform3D(),
            "AxisAngleRotation3D" => new AxisAngleRotation3D(),
            "VisualBrush" => new VisualBrush(),
            "ImageBrush" => throw Fail(lineInfo, $"<{tag}> stays a precise boundary (binds image pixels the engine does not load)."),
            _ => throw Fail(lineInfo, $"<{tag}> is not a supported 3D node (models, visuals, cameras, lights, materials, transforms and brushes)."),
        };
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                if (reader.Prefix == "mc" || reader.Prefix == "d" || reader.Prefix == "presentationOptions") continue;
                if ((reader.Prefix == "x" && reader.LocalName == "Name") ||
                    (reader.Prefix == string.Empty && reader.LocalName == "Name"))
                {
                    if (names.ContainsKey(reader.Value)) throw Fail(lineInfo, $"Duplicate x:Name '{reader.Value}'.");
                    names[reader.Value] = node;
                    continue;
                }
                if (reader.Prefix == "x")
                    throw Fail(lineInfo, $"x:{reader.LocalName} is not supported on 3D nodes (use x:Name).");
                if (node is VisualBrush visualBrush)
                {
                    ApplyVisualBrushAttribute(visualBrush, reader.Name, reader.Value, lineInfo, visuals);
                    continue;
                }
                ApplyModelAttribute(node, tag, reader.Name, reader.Value, lineInfo);
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) { reader.Read(); return node; }
        reader.Read(); // past the node start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, $"Unterminated <{tag}>.");
            if (reader.LocalName.Contains('.'))
                ReadModelProperty(node, tag, reader, lineInfo, names, wires, visuals);
            else if (node is ModelVisual3D model && reader.LocalName == "ModelVisual3D")
                model.Children.Add((ModelVisual3D)ReadModelNode(reader, lineInfo, names, wires, visuals));
            else if (node is Transform3DGroup group)
                group.Children.Add((Transform3D)ReadModelNode(reader, lineInfo, names, wires, visuals));
            else
                throw Fail(lineInfo, $"<{reader.LocalName}> cannot nest in <{tag}> (use the matching property element).");
        }
        reader.Read(); // past the node end tag
        return node;
    }

    private static void ReadModelProperty(object node, string tag, XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        var parts = reader.LocalName.Split('.', 2);
        var owner = parts[0];
        var property = parts[1];
        var expected = node.GetType().Name;
        if (!string.Equals(owner, expected, StringComparison.Ordinal))
            throw Fail(lineInfo, $"<{reader.LocalName}> does not belong to <{expected}>.");
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the property start tag
        if (node is ModelVisual3D childrenModel && property == "Children")
        {
            while (true)
            {
                SkipWhitespace(reader);
                if (reader.NodeType == XmlNodeType.EndElement) break;
                if (reader.EOF) throw Fail(lineInfo, $"Unterminated <{owner}.{property}>.");
                if (reader.NodeType != XmlNodeType.Element)
                    throw Fail(lineInfo, $"<{owner}.{property}> needs nested 3D elements.");
                var nested = ReadModelNode(reader, lineInfo, names, wires, visuals);
                if (nested is not ModelVisual3D nestedModel)
                    throw Fail(lineInfo, $"<{nested.GetType().Name}> does not belong in <{owner}.{property}> (ModelVisual3D only).");
                childrenModel.Children.Add(nestedModel);
            }
            reader.Read(); // past the property end tag
            return;
        }
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element)
            throw Fail(lineInfo, $"<{reader.LocalName}> needs a nested 3D element.");
        var child = ReadModelNode(reader, lineInfo, names, wires, visuals);
        switch (node)
        {
            case ModelVisual3D model when property == "Content" && child is Model3D content:
                model.Content = content; break;
            case ModelVisual3D model when property == "Transform" && child is Transform3D transform:
                model.Transform = transform; break;
            case GeometryModel3D geometry when property == "Geometry" && child is Geometry3D mesh:
                geometry.Geometry = mesh; break;
            case GeometryModel3D geometry when property is "Material" or "BackMaterial" && child is Material material:
                if (property == "Material") geometry.Material = material;
                else geometry.BackMaterial = material;
                break;
            case DiffuseMaterial diffuse when property == "Brush" && child is VisualBrush visual:
                diffuse.Brush = visual; break;
            case DiffuseMaterial _ when property == "Brush":
                throw Fail(lineInfo, "<DiffuseMaterial.Brush> needs VisualBrush content (ImageBrush binds image pixels the engine does not load).");
            case RotateTransform3D rotate when property == "Rotation" && child is Rotation3D rotation:
                rotate.Rotation = rotation; break;
            default:
                throw Fail(lineInfo, $"<{reader.LocalName}> does not belong here (wrong owner or content type).");
        }
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"Malformed <{owner}.{property}>.");
        reader.Read(); // past the property end tag
    }

    /// <summary>Applies one VisualBrush attribute (tile knobs plus the deferred Visual binding).</summary>
    private static void ApplyVisualBrushAttribute(VisualBrush brush, string name, string value, IXmlLineInfo lineInfo, List<PendingVisual> visuals)
    {
        // Render caching hints have no engine effect (the wireframe renderer is
        // unlit; same accept-and-ignore policy as Window.SizeToContent).
        if (name.StartsWith("RenderOptions.", StringComparison.Ordinal)) return;
        switch (name)
        {
            case "Visual":
            {
                var text = value.Trim();
                if (!text.StartsWith('{') || !text.EndsWith('}'))
                    throw Fail(lineInfo, "'Visual' needs {Binding ElementName=...} (inline visuals are not supported).");
                var inner = text[1..^1].Trim();
                if (!inner.StartsWith("Binding", StringComparison.Ordinal))
                    throw Fail(lineInfo, "'Visual' needs {Binding ElementName=...} (inline visuals are not supported).");
                Binding binding;
                try { binding = ParseBinding(inner["Binding".Length..], text, lineInfo); }
                catch (XamlLoadException) { throw; }
                if (binding.ElementName is null || binding.Path.Path.Length != 0)
                    throw Fail(lineInfo, "'Visual' needs {Binding ElementName=...} (path bindings are not supported).");
                visuals.Add(new PendingVisual(brush, binding.ElementName));
                return;
            }
            case "Viewport":
                try { brush.Viewport = RectangleGeometry.ParseRect(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "ViewportUnits": brush.ViewportUnits = ParseBrushMapping(value, lineInfo); return;
            case "Viewbox":
                try { brush.Viewbox = RectangleGeometry.ParseRect(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "ViewboxUnits": brush.ViewboxUnits = ParseBrushMapping(value, lineInfo); return;
            case "TileMode":
                if (!Enum.TryParse<TileMode>(value, ignoreCase: false, out var tile))
                    throw Fail(lineInfo, $"'{value}' is not a TileMode (None/Tile/FlipX/FlipY/FlipXY).");
                brush.TileMode = tile; return;
            case "Stretch":
                if (!Enum.TryParse<Stretch>(value, ignoreCase: true, out var stretch))
                    throw Fail(lineInfo, $"'{value}' is not a Stretch (None/Fill/Uniform/UniformToFill).");
                brush.Stretch = stretch; return;
            case "Opacity": brush.Opacity = ParseDouble(value, lineInfo); return;
        }
        throw Fail(lineInfo, $"'{name}' is not valid on <VisualBrush> (Visual/Viewport/ViewportUnits/Viewbox/ViewboxUnits/TileMode/Stretch/Opacity).");
    }

    private static void ApplyModelAttribute(object node, string tag, string name, string value, IXmlLineInfo lineInfo)
    {
        switch (name)
        {
            case "Positions" when node is MeshGeometry3D mesh:
                try { mesh.Positions = Point3DCollection.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "TriangleIndices" when node is MeshGeometry3D mesh:
                try { mesh.TriangleIndices = Int32Collection.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "TextureCoordinates" when node is MeshGeometry3D mesh:
                try { mesh.TextureCoordinates = PointCollection.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "Axis" when node is AxisAngleRotation3D rotation:
                try { rotation.Axis = Vector3D.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "Angle" when node is AxisAngleRotation3D rotation:
                rotation.Angle = ParseDouble(value, lineInfo); return;
            case "CenterX" or "CenterY" or "CenterZ" when node is RotateTransform3D rotate:
                SetAxisComponent(v => rotate.CenterX = v, v => rotate.CenterY = v, v => rotate.CenterZ = v, name, value, lineInfo); return;
            case "ScaleX" or "ScaleY" or "ScaleZ" when node is ScaleTransform3D scale:
                SetAxisComponent(v => scale.ScaleX = v, v => scale.ScaleY = v, v => scale.ScaleZ = v, name, value, lineInfo); return;
            case "CenterX" or "CenterY" or "CenterZ" when node is ScaleTransform3D scale:
                SetAxisComponent(v => scale.CenterX = v, v => scale.CenterY = v, v => scale.CenterZ = v, name, value, lineInfo); return;
            case "OffsetX" or "OffsetY" or "OffsetZ" when node is TranslateTransform3D translate:
                SetAxisComponent(v => translate.OffsetX = v, v => translate.OffsetY = v, v => translate.OffsetZ = v, name, value, lineInfo); return;
            case "Position" when node is PerspectiveCamera camera:
                try { camera.Position = Point3D.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "LookDirection" when node is PerspectiveCamera camera:
                try { camera.LookDirection = Vector3D.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "UpDirection" when node is PerspectiveCamera camera:
                try { camera.UpDirection = Vector3D.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "FieldOfView" when node is PerspectiveCamera camera:
                camera.FieldOfView = ParseDouble(value, lineInfo); return;
            case "NearPlaneDistance" when node is PerspectiveCamera camera:
                camera.NearPlaneDistance = ParseDouble(value, lineInfo); return;
            case "FarPlaneDistance" when node is PerspectiveCamera camera:
                camera.FarPlaneDistance = ParseDouble(value, lineInfo); return;
            case "Color" when node is Light light:
                try { light.Color = Color.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "Color" when node is DiffuseMaterial diffuse:
                try { diffuse.Color = Color.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
            case "Direction" when node is DirectionalLight directional:
                try { directional.Direction = Vector3D.Parse(value); return; }
                catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
        }
        throw Fail(lineInfo, $"'{name}' is not valid on <{tag}>.");
    }

    private static void SetAxisComponent(Action<double> setX, Action<double> setY, Action<double> setZ, string name, string value, IXmlLineInfo lineInfo)
    {
        var parsed = ParseDouble(value, lineInfo);
        if (name.EndsWith("X", StringComparison.Ordinal)) setX(parsed);
        else if (name.EndsWith("Y", StringComparison.Ordinal)) setY(parsed);
        else setZ(parsed);
    }

    /// <summary>
    /// Reads an element Resources property (positioned on the property start
    /// tag): a single ResourceDictionary that replaces the element scope.
    /// </summary>
    private static void ReadElementResources(XmlReader reader, IXmlLineInfo lineInfo, FrameworkElement target, List<XamlEventWire>? wires = null)
    {
        if (reader.IsEmptyElement)
            throw Fail(lineInfo, $"<{target.GetType().Name}.Resources/> is empty (a <ResourceDictionary> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element)
            throw Fail(lineInfo, $"<{target.GetType().Name}.Resources> needs resource entries.");
        if (reader.LocalName == "ResourceDictionary") target.SetResources(ReadResourceDictionaryRoot(reader, lineInfo, depth: 0, wires));
        else
        {
            var dictionary = new ResourceDictionary();
            while (reader.NodeType == XmlNodeType.Element) { ReadResourceEntry(reader, lineInfo, dictionary, wires); SkipWhitespace(reader); }
            target.SetResources(dictionary);
        }
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"Malformed <{target.GetType().Name}.Resources> (one dictionary only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>
    /// Reads an element Style property (positioned on the property start tag):
    /// a single nested Style applied immediately with load-time wire capture.
    /// </summary>
    private static void ReadElementStyle(XmlReader reader, IXmlLineInfo lineInfo, FrameworkElement target, List<XamlEventWire> wires, List<FrameworkElement> scope)
    {
        if (reader.IsEmptyElement)
            throw Fail(lineInfo, $"<{target.GetType().Name}.Style/> is empty (a <Style> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Style")
            throw Fail(lineInfo, $"<{target.GetType().Name}.Style> needs a nested <Style>.");
        var style = ReadStyleElement(reader, lineInfo);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"Malformed <{target.GetType().Name}.Style> (one Style only).");
        reader.Read(); // past the property end tag
        ApplyStyleResource(target, style, lineInfo, wires, scope);
    }

    /// <summary>
    /// Reads an ItemsControl.ItemContainerStyle property (positioned on the
    /// property start tag): a single nested Style assigned to the control,
    /// like WPF (container application itself stays a boundary).
    /// </summary>
    private static void ReadElementItemContainerStyle(XmlReader reader, IXmlLineInfo lineInfo, ItemsControl target)
    {
        if (reader.IsEmptyElement)
            throw Fail(lineInfo, "<ItemsControl.ItemContainerStyle/> is empty (a <Style> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Style")
            throw Fail(lineInfo, "<ItemsControl.ItemContainerStyle> needs a nested <Style>.");
        target.ItemContainerStyle = ReadStyleElement(reader, lineInfo);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, "Malformed <ItemsControl.ItemContainerStyle> (one Style only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>
    /// Reads an Image Source property (positioned on the property start tag):
    /// a single nested BitmapImage applied immediately.
    /// </summary>
    private static void ReadImageSource(XmlReader reader, IXmlLineInfo lineInfo, Image target)
    {
        if (reader.IsEmptyElement)
            throw Fail(lineInfo, "<Image.Source/> is empty (a <BitmapImage> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "BitmapImage")
            throw Fail(lineInfo, "<Image.Source> needs a nested <BitmapImage>.");
        var uri = reader.GetAttribute("UriSource");
        var bitmap = new BitmapImage();
        try
        {
            if (uri is not null) bitmap.UriSource = new Uri(uri, UriKind.RelativeOrAbsolute);
        }
        catch (UriFormatException ex) { throw Fail(lineInfo, ex.Message); }
        reader.Skip();
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, "Malformed <Image.Source> (one BitmapImage only).");
        reader.Read(); // past the property end tag
        target.Source = bitmap;
    }

    /// <summary>
    /// Parses a standalone ResourceDictionary document (Source= included).
    /// </summary>
    public static ResourceDictionary LoadResourceDictionary(string xaml) =>
        LoadResourceDictionary(xaml, depth: 0);

    internal static ResourceDictionary LoadResourceDictionary(string xaml, int depth)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        using var reader = XmlReader.Create(new StringReader(xaml), ReaderSettings);
        var lineInfo = (IXmlLineInfo)reader;
        MoveToContentElement(reader, lineInfo);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ResourceDictionary")
            throw Fail(lineInfo, "Resource root must be <ResourceDictionary>.");
        var dict = ReadResourceDictionaryRoot(reader, lineInfo, depth);
        SkipWhitespace(reader);
        if (!reader.EOF)
            throw Fail(lineInfo, "Unexpected trailing content after the resource dictionary.");
        return dict;
    }

    /// <summary>
    /// Reads a DataTemplate document (positioned before its root; consumes
    /// through its end tag). Converted code loads template XAML at runtime
    /// through XamlReader, like WPF.
    /// </summary>
    internal static DataTemplate LoadDataTemplate(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        using var reader = XmlReader.Create(new StringReader(xaml), ReaderSettings);
        var lineInfo = (IXmlLineInfo)reader;
        MoveToContentElement(reader, lineInfo);
        var template = ReadDataTemplateElement(reader, lineInfo);
        SkipWhitespace(reader);
        if (!reader.EOF)
            throw Fail(lineInfo, "Unexpected trailing content after the data template.");
        return template;
    }

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads a ResourceDictionary element (positioned on its start tag;
    /// consumes through its end tag). Inline entries, Source= loading from
    /// the Application pack store, and MergedDictionaries are supported.
    /// </summary>
    /// <summary>
    /// Hydration scopes for load-time StaticResource (outermost first): every
    /// loose-XAML dictionary read pushes itself, so StaticResource entries in
    /// a merged Source dictionary resolve against the dictionaries already
    /// merged around them, like WPF. Emptied by the finally below, so
    /// attribute-time StaticResource resolution (after hydration) is unaffected.
    /// </summary>
    private static readonly List<ResourceDictionary> HydrationScopes = new();

    /// <summary>Tries the hydration scopes innermost-first (load-time StaticResource).</summary>
    internal static bool TryFindHydrationResource(object key, out object? value)
    {
        for (var i = HydrationScopes.Count - 1; i >= 0; i--)
        {
            if (HydrationScopes[i].TryGetValue(key, out value) && value is not null) return true;
        }
        value = null;
        return false;
    }

    internal static ResourceDictionary ReadResourceDictionaryRoot(XmlReader reader, IXmlLineInfo lineInfo, int depth, List<XamlEventWire>? wires = null)
    {
        if (depth > 8)
            throw Fail(lineInfo, "ResourceDictionary nesting is too deep (possible Source= cycle).");
        var dict = new ResourceDictionary();
        HydrationScopes.Add(dict);
        try
        {
            ReadResourceDictionaryContent(dict, reader, lineInfo, depth, wires);
            return dict;
        }
        finally
        {
            HydrationScopes.RemoveAt(HydrationScopes.Count - 1);
        }
    }

    private static void ReadResourceDictionaryContent(ResourceDictionary dict, XmlReader reader, IXmlLineInfo lineInfo, int depth, List<XamlEventWire>? wires)
    {
        if (depth > 8)
            throw Fail(lineInfo, "ResourceDictionary nesting is too deep (possible Source= cycle).");
        string? source = null;
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                if (reader.Prefix == string.Empty && reader.LocalName == "Source") { source = reader.Value; continue; }
                throw Fail(lineInfo, $"'{reader.Name}' is not valid on <ResourceDictionary> (Source only).");
            }
            reader.MoveToElement();
        }
        if (source is not null)
        {
            if (!reader.IsEmptyElement)
                throw Fail(lineInfo, "<ResourceDictionary> with Source= cannot hold inline entries.");
            LoadSourceDictionary(dict, source, lineInfo, depth);
            reader.Read(); // past the empty element
            return;
        }
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <ResourceDictionary>.");
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(lineInfo, "Expected a resource entry inside <ResourceDictionary>.");
            if (reader.LocalName.Contains('.'))
            {
                if (reader.LocalName != "ResourceDictionary.MergedDictionaries")
                    throw Fail(lineInfo, $"<{reader.LocalName}> is not valid inside <ResourceDictionary>.");
                ReadMergedDictionaries(reader, lineInfo, dict, depth, wires);
                continue;
            }
            ReadResourceEntry(reader, lineInfo, dict, wires);
        }
        reader.Read(); // past the dictionary end tag
        return;
    }

    private static void ReadMergedDictionaries(XmlReader reader, IXmlLineInfo lineInfo, ResourceDictionary dict, int depth, List<XamlEventWire>? wires = null)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the property start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <ResourceDictionary.MergedDictionaries>.");
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(lineInfo, "Merged dictionaries hold <ResourceDictionary Source=\"...\"/> entries only.");
            if (reader.LocalName != "ResourceDictionary")
            {
                // Vendor theme dictionaries (materialDesign:BundledTheme):
                // ResourceDictionary-derived objects merge like WPF.
                dict.MergedDictionaries.Add(ReadMergedDictionaryObject(reader, lineInfo));
                continue;
            }
            dict.MergedDictionaries.Add(ReadResourceDictionaryRoot(reader, lineInfo, depth + 1, wires));
        }
        reader.Read(); // past the property end tag
    }

    /// <summary>
    /// Reads one ResourceDictionary-derived merged child
    /// (materialDesign:BundledTheme): resolves the converted type through
    /// the prefixed-type resolver, applies plain and {prefix:Extension}
    /// attributes, and returns the instance to merge. Nested content
    /// stays a precise boundary (the vendor themes take attributes only).
    /// </summary>
    private static ResourceDictionary ReadMergedDictionaryObject(XmlReader reader, IXmlLineInfo lineInfo)
    {
        var prefix = reader.Prefix;
        var node = XElement.Parse(reader.ReadOuterXml());
        var local = node.Name.LocalName;
        var original = prefix.Length == 0 ? local : prefix + ":" + local;
        var xmlns = (string name) => node.GetNamespaceOfPrefix(name)?.NamespaceName;
        var type = ResolveConvertedTypeRef(original, lineInfo, xmlns, original);
        if (!typeof(ResourceDictionary).IsAssignableFrom(type))
            throw Fail(lineInfo, "Merged <" + original + "> is <" + type.Name + ">, not a ResourceDictionary.");
        if (!HasDefaultCtor(type))
            throw Fail(lineInfo, "Merged <" + original + "> needs a parameterless constructor.");
        if (node.HasElements || !string.IsNullOrWhiteSpace(node.Value))
            throw Fail(lineInfo, "Merged <" + original + "> takes attributes only (no nested content).");
        var instance = (ResourceDictionary)Activator.CreateInstance(type, nonPublic: true)!;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace)
                throw Fail(lineInfo, "x:" + attribute.Name.LocalName + " is not valid on merged <" + original + "> (merged entries take no key).");
            var raw = attribute.Value.Trim();
            if (raw.StartsWith("{", StringComparison.Ordinal) && raw.EndsWith("}", StringComparison.Ordinal))
            {
                var prop = type.GetProperty(attribute.Name.LocalName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?? throw Fail(lineInfo, "Merged property " + attribute.Name.LocalName + " is not settable on <" + type.Name + ">.");
                if (!prop.CanWrite || prop.SetMethod?.IsPublic != true)
                    throw Fail(lineInfo, "Merged property " + attribute.Name.LocalName + " is not settable on <" + type.Name + ">.");
                var extended = EvaluateConvertedExtensionAttribute(raw, instance, prop, xmlns, lineInfo);
                if (!prop.PropertyType.IsInstanceOfType(extended))
                    throw Fail(lineInfo, "Extension " + raw + " does not fit merged property " + prop.Name + ".");
                prop.SetValue(instance, extended);
                continue;
            }
            if (!TrySetPlainObjectProperty(instance, attribute.Name.LocalName, raw))
                throw Fail(lineInfo, "Merged property " + attribute.Name.LocalName + " is not settable on <" + type.Name + ">.");
        }
        return instance;
    }

    /// <summary>
    /// Evaluates a {prefix:Extension ...} attribute on a merged dictionary
    /// child: instantiates the converted MarkupExtension, applies
    /// Name=Value arguments, and returns ProvideValue against the target
    /// property. Anything but a converted extension head is a precise
    /// error (resource and binding extensions cannot run before the merge).
    /// </summary>
    private static object? EvaluateConvertedExtensionAttribute(
        string raw, object instance, System.Reflection.PropertyInfo prop,
        Func<string, string?> xmlns, IXmlLineInfo lineInfo)
    {
        var inner = raw[1..^1].Trim();
        var head = inner.Split(new[] { ' ', '\t', ',' }, 2)[0];
        if (!head.Contains(':'))
            throw Fail(lineInfo, "Extension " + raw + " needs a converted {prefix:Extension} head.");
        var type = ResolveConvertedTypeRef(head, lineInfo, xmlns, raw);
        if (!typeof(MarkupExtension).IsAssignableFrom(type))
            throw Fail(lineInfo, "Extension " + raw + " names <" + type.Name + ">, not a markup extension.");
        if (!HasDefaultCtor(type))
            throw Fail(lineInfo, "Extension " + raw + " needs a parameterless constructor.");
        var extension = (MarkupExtension)Activator.CreateInstance(type, nonPublic: true)!;
        var rest = inner[head.Length..].Trim().TrimStart(',').Trim();
        if (rest.Length != 0)
        {
            foreach (var part in rest.Split(','))
            {
                var argument = part.Trim();
                var equals = argument.IndexOf('=');
                if (equals <= 0)
                    throw Fail(lineInfo, "Extension " + raw + " takes Name=Value arguments only.");
                if (!TrySetPlainObjectProperty(extension, argument[..equals].Trim(), argument[(equals + 1)..].Trim()))
                    throw Fail(lineInfo, "Extension argument " + argument + " is not settable on <" + type.Name + ">.");
            }
        }
        try { return extension.ProvideValue(new XamlExtensionService(instance, prop)); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    /// <summary>
    /// Reads one resource entry (positioned on its start tag): SolidColorBrush
    /// (Color=), Color (Color= or hex text), String/Double/Int32/Boolean
    /// (text), each requiring x:Key.
    /// </summary>
    private static void ReadResourceEntry(XmlReader reader, IXmlLineInfo lineInfo, ResourceDictionary dict, List<XamlEventWire>? wires = null)
    {
        if (TryReadApplicationResource(reader, lineInfo, dict)) return;
        var entry = reader.LocalName;
        if (entry is "DrawingBrush" or "GradientStopCollection" or "LinearGradientBrush" or "RadialGradientBrush")
        {
            ReadBrushResourceEntry(reader, lineInfo, dict);
            return;
        }
        if (entry is "LineGeometry" or "RectangleGeometry" or "EllipseGeometry" or "GeometryGroup" or "PathGeometry" or "CombinedGeometry")
        {
            var node = XElement.Parse(reader.ReadOuterXml());
            var geometryKey = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
                ?? throw Fail(lineInfo, $"<{entry}> resource entry needs x:Key.");
            try { dict.Add(geometryKey, ReadGeometryNode(node, lineInfo)); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            return;
        }
        if (entry is "Geometry")
        {
            // Path-mini-language resource (the gallery's GitHub icon geometry):
            // <Geometry x:Key="...">M...z</Geometry> parses like Path.Data.
            var node = XElement.Parse(reader.ReadOuterXml());
            var pathKey = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
                ?? throw Fail(lineInfo, "<Geometry> resource entry needs x:Key.");
            if (node.HasElements || string.IsNullOrWhiteSpace(node.Value))
                throw Fail(lineInfo, "<Geometry> resource entry needs path data text.");
            try { dict.Add(pathKey, Geometry.Parse(node.Value.Trim())); }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                throw Fail(lineInfo, ex.Message);
            }
            return;
        }
        if (TryReadEffectResourceEntry(reader, lineInfo, dict)) return;
        if (FindEngineResourceType(entry) is { } resourceType)
        {
            // Theme converter instances (Fluent packs
            // BooleanToVisibilityConverter as a resource): engine-owned
            // converter types instantiated with attribute-converted props.
            var node = XElement.Parse(reader.ReadOuterXml());
            var converterKey = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
                ?? throw Fail(lineInfo, $"<{entry}> resource entry needs x:Key.");
            if (node.HasElements || !string.IsNullOrWhiteSpace(node.Value))
                throw Fail(lineInfo, $"<{entry}> resource entry takes attributes only.");
            var resourceInstance = Activator.CreateInstance(resourceType)
                ?? throw Fail(lineInfo, $"Cannot create resource '{entry}'.");
            foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName != XamlNamespace))
            {
                var property = resourceType.GetProperty(attribute.Name.LocalName)
                    ?? throw Fail(lineInfo, $"Unknown resource property '{attribute.Name.LocalName}'.");
                if (property.SetMethod?.IsPublic != true) throw Fail(lineInfo, $"Resource property '{property.Name}' is not writable.");
                property.SetValue(resourceInstance, BindingExpression.ConvertValue(attribute.Value, property.PropertyType));
            }
            try { dict.Add(converterKey, resourceInstance); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            return;
        }
        if (entry is "ImageBrush")
        {
            // Image resource (gallery tile/dashboard artwork): pack or
            // relative ImageSource resolves through the manifest store like
            // Image, plus the TileBrush layout knobs.
            var node = XElement.Parse(reader.ReadOuterXml());
            var imageKey = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
                ?? throw Fail(lineInfo, "<ImageBrush> resource entry needs x:Key.");
            try { dict.Add(imageKey, ReadImageBrushNode(node, lineInfo)); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            return;
        }
        if (entry is "SineEase" or "CubicEase" or "PowerEase")
        {
            // Easing resources (badge/progress pulses): engine easing
            // instances keyed for EasingFunction={StaticResource Key}.
            var ease = (EasingFunctionBase)(entry switch
            {
                "SineEase" => new SineEase(),
                "CubicEase" => new CubicEase(),
                _ => new PowerEase(),
            });
            string? easeKey = null;
            var easeFreeze = false;
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == "x" && reader.LocalName == "Key")
                    {
                        if (easeKey is not null) throw Fail(lineInfo, "Duplicate x:Key on a resource entry.");
                        easeKey = reader.Value;
                        continue;
                    }
                    if (reader.Prefix == string.Empty)
                    {
                        var easeProp = ease.GetType().GetProperty(reader.LocalName,
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        if (easeProp is not null && easeProp.CanWrite)
                        {
                            easeProp.SetValue(ease, BindingExpression.ConvertValue(reader.Value, easeProp.PropertyType));
                            continue;
                        }
                    }
                    if (reader.NamespaceURI.EndsWith("/presentation/options", StringComparison.Ordinal))
                    {
                        if (reader.LocalName == "Freeze" && bool.TryParse(reader.Value, out var parsed)) easeFreeze = parsed;
                        continue;
                    }
                    throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{entry}> resource entry (x:Key plus easing properties).");
                }
                reader.MoveToElement();
            }
            if (easeKey is null)
                throw Fail(lineInfo, $"<{entry}> resource entry needs x:Key.");
            if (!reader.IsEmptyElement)
                throw Fail(lineInfo, $"<{entry}> resource entry holds attributes only.");
            reader.Read();
            if (easeFreeze && !ease.IsFrozen) ease.Freeze();
            try { dict.Add(easeKey, ease); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            return;
        }
        string? key = null;
        string? colorAttr = null;
        string? opacityAttr = null;
        string? colorDynamicKey = null;
        // Timeline entries (Storyboard/DoubleAnimation) collect every plain
        // attribute for the timeline applier below (dotted Storyboard.Target*
        // names included); scalar entries keep the strict Color=/x:Key rule.
        var timelineAttrs = entry is "Storyboard" or "DoubleAnimation"
            ? new List<(string Name, string Value)>()
            : null;
        string? bitmapUri = null;
        var freeze = false;
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                if (reader.Prefix == "x" && reader.LocalName == "Key")
                {
                    if (key is not null) throw Fail(lineInfo, "Duplicate x:Key on a resource entry.");
                    key = reader.Value;
                    continue;
                }
                if (entry == "BitmapImage" && reader.Prefix == string.Empty && reader.LocalName == "UriSource")
                {
                    bitmapUri = reader.Value;
                    continue;
                }
                if (timelineAttrs is not null && reader.Prefix != "x")
                {
                    timelineAttrs.Add((reader.Name, reader.Value));
                    continue;
                }
                if (reader.Prefix == "x")
                    throw Fail(lineInfo, $"x:{reader.LocalName} is not supported on resource entries (use x:Key).");
                if (reader.Prefix == string.Empty && reader.LocalName == "Color" &&
                    (entry is "SolidColorBrush" or "Color"))
                {
                    if (reader.Value.StartsWith("{StaticResource ", StringComparison.Ordinal) && reader.Value.EndsWith('}'))
                    {
                        var staticKey = reader.Value["{StaticResource ".Length..^1].Trim();
                        if (staticKey.Length == 0 || staticKey.Contains('{') || staticKey.Contains('='))
                            throw Fail(lineInfo, $"'{reader.Value}' takes a plain key (Color={{StaticResource Key}}).");
                        colorAttr = ResolveEntryStaticColor(staticKey, lineInfo);
                    }
                    else if (reader.Value.StartsWith("{DynamicResource ", StringComparison.Ordinal) && reader.Value.EndsWith('}'))
                    {
                        var dynamicKey = reader.Value["{DynamicResource ".Length..^1].Trim();
                        if (dynamicKey.Length == 0 || dynamicKey.Contains('{') || dynamicKey.Contains('='))
                            throw Fail(lineInfo, $"'{reader.Value}' takes a plain key (Color={{DynamicResource Key}}).");
                        colorDynamicKey = dynamicKey;
                    }
                    else colorAttr = reader.Value;
                    continue;
                }
                if (reader.Prefix == string.Empty && reader.LocalName == "Opacity" &&
                    entry is "SolidColorBrush") { opacityAttr = reader.Value; continue; }
                if (reader.NamespaceURI.EndsWith("/presentation/options", StringComparison.Ordinal))
                {
                    // Designer freeze hint (theme SolidColorBrush aliases):
                    // honored below like the BAML loader freeze markers.
                    if (reader.LocalName == "Freeze" && bool.TryParse(reader.Value, out var parsed)) freeze = parsed;
                    continue;
                }
                throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{entry}> resource entry (x:Key plus Color= for brushes).");
            }
            reader.MoveToElement();
        }
        if (key is null)
            throw Fail(lineInfo, $"<{entry}> resource entry needs x:Key.");
        if (timelineAttrs is not null)
        {
            var timeline = ReadTimelineEntry(reader, lineInfo, entry, timelineAttrs, wires,
                key => dict.Contains(key) ? dict[key] : null);
            try { dict.Add(key, timeline); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            return;
        }
        if (entry == "BitmapImage")
        {
            if (bitmapUri is null) throw Fail(lineInfo, "<BitmapImage> resource entry needs UriSource=.");
            BitmapImage bitmap;
            try { bitmap = new BitmapImage(new Uri(bitmapUri, UriKind.RelativeOrAbsolute)); }
            catch (UriFormatException ex) { throw Fail(lineInfo, ex.Message); }
            try { dict.Add(key ?? throw Fail(lineInfo, "<BitmapImage> resource entry needs x:Key."), bitmap); }
            catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
            reader.MoveToElement();
            if (reader.IsEmptyElement) reader.Read();
            else reader.Skip();
            return;
        }
        var text = ReadEntryText(reader, lineInfo, entry);
        object value = entry switch
        {
            "SolidColorBrush" when (colorAttr is not null || colorDynamicKey is not null) && text.Length == 0 => ReadSolidColorBrushEntry(colorAttr, colorDynamicKey, opacityAttr, lineInfo),
            "SolidColorBrush" => throw Fail(lineInfo, "<SolidColorBrush> takes Color=\"...\" (no nested content)."),
            "Color" when colorAttr is not null && text.Length == 0 => ParseColor(colorAttr, lineInfo),
            "Color" when colorDynamicKey is not null && text.Length == 0 => ParseColor(ResolveEntryDynamicColor(colorDynamicKey, lineInfo), lineInfo),
            "Color" when colorAttr is null && text.Length > 0 => ParseColor(text, lineInfo),
            "Color" => throw Fail(lineInfo, "<Color> takes Color=\"...\" or hex text, not both/neither."),
            "String" => text,
            "Double" => ParseDouble(text, lineInfo),
            "Int32" => ParseInt(text, lineInfo),
            "Boolean" => ParseBool(text, lineInfo),
            "CornerRadius" => ParseCornerRadiusEntry(text, lineInfo),
            "Thickness" => ParseThickness(text, lineInfo),
            "FontFamily" => new FontFamily(text),
            _ => throw Fail(lineInfo, $"<{entry}> is not a supported resource entry (BitmapImage/SolidColorBrush/Color/String/Double/Int32/Boolean/CornerRadius/Thickness/FontFamily/SineEase/CubicEase/PowerEase/Storyboard/DoubleAnimation/DoubleAnimationUsingKeyFrames/ObjectAnimationUsingKeyFrames)."),
        };
        if (freeze && value is Freezable freezable && !freezable.IsFrozen) freezable.Freeze();
        try { dict.Add(key, value); }
        catch (InvalidOperationException ex) { throw Fail(lineInfo, ex.Message); }
    }

    /// <summary>
    /// Reads a timeline resource entry (positioned on its start tag, attributes
    /// already collected): Storyboard entries recurse into Storyboard/
    /// DoubleAnimation/ThicknessAnimation/key-frame children; anything
    /// else is a precise error (triggers and nested easing elements stay
    /// boundaries). EasingFunction={StaticResource Key} resolves through the
    /// loading dictionary when the caller supplies it.
    /// </summary>
    private static Timeline ReadTimelineEntry(XmlReader reader, IXmlLineInfo lineInfo, string entry, List<(string Name, string Value)> attrs, List<XamlEventWire>? wires = null, Func<string, object?>? resolveResource = null)
    {
        Timeline timeline = entry switch
        {
            "Storyboard" => new Storyboard(),
            "DoubleAnimation" => new DoubleAnimation(),
            "ThicknessAnimation" => new ThicknessAnimation(),
            "DoubleAnimationUsingKeyFrames" => new DoubleAnimationUsingKeyFrames(),
            "PointAnimationUsingKeyFrames" => new PointAnimationUsingKeyFrames(),
            "ObjectAnimationUsingKeyFrames" => new ObjectAnimationUsingKeyFrames(),
            "BooleanAnimationUsingKeyFrames" => new BooleanAnimationUsingKeyFrames(),
            "ThicknessAnimationUsingKeyFrames" => new ThicknessAnimationUsingKeyFrames(),
            _ => throw Fail(lineInfo, $"<{entry}> is not a timeline resource entry."),
        };
        string? completed = null;
        foreach (var (name, value) in attrs)
        {
            // Completed= wires a code-behind method (bound by WpfAppBoot
            // through the namescope, like element events); Completed is the
            // only dotted-name-free event real apps put on timelines.
            if (name == "Completed") { completed = value; continue; }
            ApplyTimelineAttribute(timeline, name, value, lineInfo, resolveResource);
        }
        if (timeline is DoubleAnimationUsingKeyFrames keyed)
        {
            ReadDoubleKeyFrames(reader, lineInfo, keyed, resolveResource);
            return timeline;
        }
        if (timeline is PointAnimationUsingKeyFrames pointKeyed)
        {
            ReadPointKeyFrames(reader, lineInfo, pointKeyed, resolveResource);
            return timeline;
        }
        if (timeline is ObjectAnimationUsingKeyFrames objectKeyed)
        {
            ReadObjectKeyFrames(reader, lineInfo, objectKeyed, resolveResource);
            return timeline;
        }
        if (timeline is ThicknessAnimationUsingKeyFrames thicknessKeyed)
        {
            ReadThicknessKeyFrames(reader, lineInfo, thicknessKeyed, resolveResource);
            return timeline;
        }
        if (timeline is BooleanAnimationUsingKeyFrames booleanKeyed)
        {
            ReadBooleanKeyFrames(reader, lineInfo, booleanKeyed, resolveResource);
            return timeline;
        }
        if (!reader.IsEmptyElement && timeline is DoubleAnimation singleDouble)
        {
            singleDouble.EasingFunction = ReadInlineEasingProperty(reader, "DoubleAnimation", lineInfo);
            return timeline;
        }
        if (!reader.IsEmptyElement && timeline is ThicknessAnimation singleThickness)
        {
            singleThickness.EasingFunction = ReadInlineEasingProperty(reader, "ThicknessAnimation", lineInfo);
            return timeline;
        }
        if (completed is not null)
        {
            if (wires is null)
                throw Fail(lineInfo, $"'Completed' on <{entry}> can only be wired inside a loaded Window/Page (standalone resource dictionaries have no code-behind).");
            wires.Add(new XamlEventWire(timeline, "Completed", completed));
        }
        if (reader.IsEmptyElement) { reader.Read(); return timeline; }
        if (timeline is not Storyboard board)
            throw Fail(lineInfo, $"<{entry}> resource entry holds attributes only (children belong in <Storyboard>).");
        reader.Read(); // past the storyboard start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element ||
                (reader.LocalName != "Storyboard" && reader.LocalName != "DoubleAnimation" &&
                 reader.LocalName != "ThicknessAnimation" &&
                 reader.LocalName != "DoubleAnimationUsingKeyFrames" &&
                 reader.LocalName != "PointAnimationUsingKeyFrames" &&
                 reader.LocalName != "ObjectAnimationUsingKeyFrames" &&
                 reader.LocalName != "BooleanAnimationUsingKeyFrames" &&
                 reader.LocalName != "ThicknessAnimationUsingKeyFrames"))
                throw Fail(lineInfo, "<Storyboard> holds Storyboard/DoubleAnimation/ThicknessAnimation/DoubleAnimationUsingKeyFrames/ThicknessAnimationUsingKeyFrames/PointAnimationUsingKeyFrames/ObjectAnimationUsingKeyFrames/BooleanAnimationUsingKeyFrames children only (BeginStoryboard and nested easing objects stay precise boundaries).");
            var childName = reader.LocalName;
            var childAttrs = new List<(string Name, string Value)>();
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == "x")
                        throw Fail(lineInfo, $"x:{reader.LocalName} is not supported on nested timelines (key the outer <Storyboard>).");
                    childAttrs.Add((reader.Name, reader.Value));
                }
                reader.MoveToElement();
            }
            board.Children.Add((Timeline)ReadTimelineEntry(reader, lineInfo, childName, childAttrs, wires, resolveResource));
        }
        reader.Read(); // past the storyboard end tag
        return timeline;
    }

    private static void ReadDoubleKeyFrames(XmlReader reader, IXmlLineInfo lineInfo, DoubleAnimationUsingKeyFrames animation, Func<string, object?>? resolveResource)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the animation start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName is not ("EasingDoubleKeyFrame" or "LinearDoubleKeyFrame" or "DiscreteDoubleKeyFrame" or "SplineDoubleKeyFrame"))
                throw Fail(lineInfo, "<DoubleAnimationUsingKeyFrames> holds EasingDoubleKeyFrame/LinearDoubleKeyFrame/DiscreteDoubleKeyFrame/SplineDoubleKeyFrame children only.");
            var frameName = reader.LocalName;
            DoubleKeyFrame frame = frameName switch
            {
                "EasingDoubleKeyFrame" => new EasingDoubleKeyFrame(),
                "LinearDoubleKeyFrame" => new LinearDoubleKeyFrame(),
                "SplineDoubleKeyFrame" => new SplineDoubleKeyFrame(),
                _ => new DiscreteDoubleKeyFrame(),
            };
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeyTime")
                    {
                        frame.KeyTime = ParseKeyTimeOrDefer(frame, reader.Value, lineInfo, resolveResource);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "Value")
                    {
                        frame.Value = ParseDouble(reader.Value, lineInfo);
                        continue;
                    }
                    if ((reader.Prefix == "x" && reader.LocalName == "Name") ||
                        (reader.Prefix == string.Empty && reader.LocalName == "Name"))
                    {
                        // Named keyframes (Generic MousePressedToNormalScale
                        // frames): the name parses like WPF registration;
                        // nothing targets frames by name yet, so it retains
                        // no lookup (a future reference fails loud at its site).
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "EasingFunction" &&
                        frame is EasingDoubleKeyFrame easing)
                    {
                        easing.EasingFunction = ParseEasingFunctionRef(reader.Value, resolveResource, lineInfo);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeySpline" &&
                        frame is SplineDoubleKeyFrame spline)
                    {
                        try { spline.KeySpline = KeySpline.Parse(reader.Value); }
                        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                        continue;
                    }
                    throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{frameName}> (KeyTime/Value plus EasingFunction on EasingDoubleKeyFrame, KeySpline on SplineDoubleKeyFrame).");
                }
                reader.MoveToElement();
            }
                if (reader.IsEmptyElement) reader.Read(); // past the frame
                else if (frame is EasingDoubleKeyFrame easingFrame)
                    easingFrame.EasingFunction = ReadInlineEasingProperty(reader, frameName, lineInfo);
                else if (frame is SplineDoubleKeyFrame splineFrame)
                    splineFrame.KeySpline = ReadInlineKeySplineProperty(reader, frameName, lineInfo);
                else throw Fail(lineInfo, $"<{frameName}> holds attributes only (reference easing functions through {{StaticResource Key}}).");
            animation.KeyFrames.Add(frame);
        }
        reader.Read(); // past the animation end tag
    }

    /// <summary>Reads thickness key-frame children (KeyTime/Value attributes
    /// plus EasingFunction on eased frames, mirroring the double reader;
    /// spline thickness frames stay a precise boundary).</summary>
    private static void ReadThicknessKeyFrames(XmlReader reader, IXmlLineInfo lineInfo, ThicknessAnimationUsingKeyFrames animation, Func<string, object?>? resolveResource)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the animation start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName is not ("EasingThicknessKeyFrame" or "LinearThicknessKeyFrame" or "DiscreteThicknessKeyFrame"))
                throw Fail(lineInfo, "<ThicknessAnimationUsingKeyFrames> holds EasingThicknessKeyFrame/LinearThicknessKeyFrame/DiscreteThicknessKeyFrame children only.");
            var frameName = reader.LocalName;
            ThicknessKeyFrame frame = frameName switch
            {
                "EasingThicknessKeyFrame" => new EasingThicknessKeyFrame(),
                "LinearThicknessKeyFrame" => new LinearThicknessKeyFrame(),
                _ => new DiscreteThicknessKeyFrame(),
            };
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeyTime")
                    {
                        frame.KeyTime = ParseKeyTimeOrDefer(frame, reader.Value, lineInfo, resolveResource);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "Value")
                    {
                        frame.Value = ParseThickness(reader.Value, lineInfo);
                        continue;
                    }
                    if ((reader.Prefix == "x" && reader.LocalName == "Name") ||
                        (reader.Prefix == string.Empty && reader.LocalName == "Name"))
                    {
                        // Named keyframes parse like WPF registration;
                        // nothing targets frames by name yet.
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "EasingFunction" &&
                        frame is EasingThicknessKeyFrame easing)
                    {
                        easing.EasingFunction = ParseEasingFunctionRef(reader.Value, resolveResource, lineInfo);
                        continue;
                    }
                    throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{frameName}> (KeyTime/Value plus EasingFunction on EasingThicknessKeyFrame).");
                }
                reader.MoveToElement();
            }
                if (reader.IsEmptyElement) reader.Read(); // past the frame
                else if (frame is EasingThicknessKeyFrame easingFrame)
                    easingFrame.EasingFunction = ReadInlineEasingProperty(reader, frameName, lineInfo);
                else throw Fail(lineInfo, $"<{frameName}> holds attributes only (reference easing functions through {{StaticResource Key}}).");
            animation.KeyFrames.Add(frame);
        }
        reader.Read(); // past the animation end tag
    }

    /// <summary>Reads an EasingDoubleKeyFrame.EasingFunction property element
    /// (inline easing instance, like the theme switch transitions).</summary>
    private static EasingFunctionBase ReadInlineEasingProperty(XmlReader reader, string frameName, IXmlLineInfo lineInfo)
    {
        reader.Read(); // past the frame start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != frameName + ".EasingFunction")
            throw Fail(lineInfo, $"<{frameName}> holds one {frameName}.EasingFunction element (reference easing functions through {{StaticResource Key}}).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element)
            throw Fail(lineInfo, $"<{frameName}.EasingFunction> holds one easing element (SineEase/CubicEase/QuadraticEase/QuarticEase/PowerEase/CircleEase/BackEase).");
        var easing = ReadInlineEasingElement(reader, lineInfo);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{frameName}.EasingFunction> holds one easing element.");
        reader.Read(); // past the property end tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{frameName}> holds one {frameName}.EasingFunction element.");
        reader.Read(); // past the frame end tag
        return easing;
    }

    /// <summary>Reads an inline easing element (EasingMode plus BackEase.Amplitude
    /// and PowerEase.Power; attribute-only, like resource easing entries).</summary>
    private static EasingFunctionBase ReadInlineEasingElement(XmlReader reader, IXmlLineInfo lineInfo)
    {
        var name = reader.LocalName;
        EasingFunctionBase easing = name switch
        {
            "SineEase" => new SineEase(),
            "CubicEase" => new CubicEase(),
            "QuadraticEase" => new QuadraticEase(),
            "PowerEase" => new PowerEase(),
            "CircleEase" => new CircleEase(),
            "QuarticEase" => new QuarticEase(),
            "BackEase" => new BackEase(),
            _ => throw Fail(lineInfo, $"<{name}> is not a supported inline easing (SineEase/CubicEase/QuadraticEase/QuarticEase/PowerEase/CircleEase/BackEase)."),
        };
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                if (reader.Prefix == string.Empty && reader.LocalName == "EasingMode")
                {
                    if (!Enum.TryParse<EasingMode>(reader.Value.Trim(), true, out var mode))
                        throw Fail(lineInfo, $"'{reader.Value}' is not an EasingMode (EaseIn/EaseOut/EaseInOut).");
                    easing.EasingMode = mode;
                    continue;
                }
                if (reader.Prefix == string.Empty && reader.LocalName == "Amplitude" && easing is BackEase back)
                {
                    back.Amplitude = ParseDouble(reader.Value, lineInfo);
                    continue;
                }
                if (reader.Prefix == string.Empty && reader.LocalName == "Power" && easing is PowerEase power)
                {
                    power.Power = ParseDouble(reader.Value, lineInfo);
                    continue;
                }
                throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{name}> (EasingMode plus Amplitude on BackEase, Power on PowerEase).");
            }
            reader.MoveToElement();
        }
        if (!reader.IsEmptyElement)
            throw Fail(lineInfo, $"<{name}> easing holds attributes only.");
        reader.Read();
        return easing;
    }

    /// <summary>Reads an inline KeySpline property element (ControlPoint1/2
    /// point attributes on one KeySpline element, like inline easings).</summary>
    private static KeySpline ReadInlineKeySplineProperty(XmlReader reader, string frameName, IXmlLineInfo lineInfo)
    {
        reader.Read(); // past the frame start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != frameName + ".KeySpline")
            throw Fail(lineInfo, $"<{frameName}> holds one {frameName}.KeySpline element (reference key splines through attributes).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "KeySpline")
            throw Fail(lineInfo, $"<{frameName}.KeySpline> holds one KeySpline element (ControlPoint1/ControlPoint2).");
        var spline = new KeySpline();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                if (reader.Prefix == string.Empty && reader.LocalName == "ControlPoint1")
                {
                    try { spline.ControlPoint1 = Point.Parse(reader.Value); }
                    catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                    continue;
                }
                if (reader.Prefix == string.Empty && reader.LocalName == "ControlPoint2")
                {
                    try { spline.ControlPoint2 = Point.Parse(reader.Value); }
                    catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                    continue;
                }
                throw Fail(lineInfo, $"'{reader.Name}' is not valid on <KeySpline> (ControlPoint1/ControlPoint2).");
            }
            reader.MoveToElement();
        }
        if (!reader.IsEmptyElement)
            throw Fail(lineInfo, "<KeySpline> holds attributes only.");
        reader.Read(); // past the spline element
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{frameName}.KeySpline> holds one KeySpline element.");
        reader.Read(); // past the property end tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{frameName}> holds one {frameName}.KeySpline element.");
        reader.Read(); // past the frame end tag
        return spline;
    }

    /// <summary>Reads point key-frame children (KeyTime/Value attributes plus
    /// KeySpline on spline frames and EasingFunction on eased frames, mirroring
    /// the double reader).</summary>
    private static void ReadPointKeyFrames(XmlReader reader, IXmlLineInfo lineInfo, PointAnimationUsingKeyFrames animation, Func<string, object?>? resolveResource = null)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the animation start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName is not ("EasingPointKeyFrame" or "LinearPointKeyFrame" or "DiscretePointKeyFrame" or "SplinePointKeyFrame"))
                throw Fail(lineInfo, "<PointAnimationUsingKeyFrames> holds EasingPointKeyFrame/LinearPointKeyFrame/DiscretePointKeyFrame/SplinePointKeyFrame children only.");
            var frameName = reader.LocalName;
            PointKeyFrame frame = frameName switch
            {
                "EasingPointKeyFrame" => new EasingPointKeyFrame(),
                "LinearPointKeyFrame" => new LinearPointKeyFrame(),
                "SplinePointKeyFrame" => new SplinePointKeyFrame(),
                _ => new DiscretePointKeyFrame(),
            };
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeyTime")
                    {
                        frame.KeyTime = ParseKeyTimeOrDefer(frame, reader.Value, lineInfo, resolveResource);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "Value")
                    {
                        try { frame.Value = Point.Parse(reader.Value); }
                        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "EasingFunction" &&
                        frame is EasingPointKeyFrame easing)
                    {
                        easing.EasingFunction = ParseEasingFunctionRef(reader.Value, resolveResource, lineInfo);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeySpline" &&
                        frame is SplinePointKeyFrame spline)
                    {
                        try { spline.KeySpline = KeySpline.Parse(reader.Value); }
                        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                        continue;
                    }
                    throw Fail(lineInfo, $"'{reader.Name}' is not valid on <{frameName}> (KeyTime/Value plus EasingFunction on EasingPointKeyFrame, KeySpline on SplinePointKeyFrame).");
                }
                reader.MoveToElement();
            }
            if (reader.IsEmptyElement) reader.Read(); // past the frame
            else if (frame is EasingPointKeyFrame easingFrame)
                easingFrame.EasingFunction = ReadInlineEasingProperty(reader, frameName, lineInfo);
            else if (frame is SplinePointKeyFrame splineFrame)
                splineFrame.KeySpline = ReadInlineKeySplineProperty(reader, frameName, lineInfo);
            else throw Fail(lineInfo, $"<{frameName}> holds attributes only (reference easing functions through {{StaticResource Key}}).");
            animation.KeyFrames.Add(frame);
        }
        reader.Read(); // past the animation end tag
    }

    private static void ReadObjectKeyFrames(XmlReader reader, IXmlLineInfo lineInfo, ObjectAnimationUsingKeyFrames animation, Func<string, object?>? resolveResource = null)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the animation start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "DiscreteObjectKeyFrame")
                throw Fail(lineInfo, "<ObjectAnimationUsingKeyFrames> holds DiscreteObjectKeyFrame children only.");
            // Fragments re-parse standalone (hoisted declarations travel on
            // the retained root, like template resource entries).
            var frameNode = System.Xml.Linq.XElement.Parse(
                HoistTemplateNamespaces(reader.ReadOuterXml(), reader, lineInfo));
            var frame = new DiscreteObjectKeyFrame();
            foreach (var attribute in frameNode.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                if (IsIgnorableAttribute(attribute)) continue;
                var local = attribute.Name.LocalName;
                if (local == "KeyTime") frame.KeyTime = ParseKeyTimeOrDefer(frame, attribute.Value, lineInfo, resolveResource);
                else if (local == "Value") frame.Value = attribute.Value;
                else throw Fail(lineInfo, $"'{attribute.Name}' is not valid on <DiscreteObjectKeyFrame> (KeyTime/Value only).");
            }
            foreach (var child in frameNode.Elements())
            {
                if (child.Name.LocalName == "DiscreteObjectKeyFrame.Value" && frame.Value is null)
                {
                    var valueElements = child.Elements().ToList();
                    if (valueElements.Count != 1)
                        throw Fail(lineInfo, "<DiscreteObjectKeyFrame.Value> holds one value element.");
                    var valueElement = valueElements[0];
                    frame.Value = ParseObjectKeyFrameValue(valueElement.Name.LocalName, valueElement.Value, lineInfo);
                    continue;
                }
                throw Fail(lineInfo, $"<{child.Name.LocalName}> is not valid in <DiscreteObjectKeyFrame> (DiscreteObjectKeyFrame.Value only, and Value once).");
            }
            animation.KeyFrames.Add(frame);
        }
        reader.Read(); // past the animation end tag
    }

    /// <summary>Parses an object key-frame value element (enums by name;
    /// booleans/doubles/ints/strings literally; anything else stays loud).</summary>
    private static object ParseObjectKeyFrameValue(string typeName, string text, IXmlLineInfo lineInfo)
    {
        var type = FindEngineType(typeName) ?? FindConvertedType(typeName);
        if (type is not null && type.IsEnum)
        {
            try { return Enum.Parse(type, text.Trim(), ignoreCase: true); }
            catch (ArgumentException ex) { throw Fail(lineInfo, ex.Message); }
        }
        if (type is null && typeName == "Boolean" && bool.TryParse(text.Trim(), out var boolean)) return boolean;
        if (type is null && typeName == "Double") return ParseDouble(text.Trim(), lineInfo);
        if (type is null && typeName == "Int32" && int.TryParse(text.Trim(), out var integer)) return integer;
        if (type is null && typeName == "String") return text;
        throw Fail(lineInfo, $"<{typeName}> is not a supported object key-frame value (engine/converted enums, Boolean/Double/Int32/String).");
    }

    private static void ReadBooleanKeyFrames(XmlReader reader, IXmlLineInfo lineInfo, BooleanAnimationUsingKeyFrames animation, Func<string, object?>? resolveResource = null)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the animation start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "DiscreteBooleanKeyFrame")
                throw Fail(lineInfo, "<BooleanAnimationUsingKeyFrames> holds DiscreteBooleanKeyFrame children only.");
            var frame = new DiscreteBooleanKeyFrame();
            if (reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                        reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                    if (reader.Prefix == string.Empty && reader.LocalName == "KeyTime")
                    {
                        frame.KeyTime = ParseKeyTimeOrDefer(frame, reader.Value, lineInfo, resolveResource);
                        continue;
                    }
                    if (reader.Prefix == string.Empty && reader.LocalName == "Value")
                    {
                        frame.Value = ParseBool(reader.Value, lineInfo);
                        continue;
                    }
                    throw Fail(lineInfo, $"'{reader.Name}' is not valid on <DiscreteBooleanKeyFrame> (KeyTime/Value only).");
                }
                reader.MoveToElement();
            }
            if (reader.IsEmptyElement) reader.Read(); // past the frame
            else throw Fail(lineInfo, "<DiscreteBooleanKeyFrame> holds attributes only.");
            animation.KeyFrames.Add(frame);
        }
        reader.Read(); // past the animation end tag
    }

    private static KeyTime ParseKeyTime(string value, IXmlLineInfo lineInfo)
    {
        try { return KeyTime.ParseKeyTimeText(value); }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        { throw Fail(lineInfo, ex.Message); }
    }

    /// <summary>Reads a plain StaticResource/DynamicResource key (timeline
    /// Duration references); false for literal values.</summary>
    private static bool TryTimelineResourceKey(string value, out string key)
    {
        key = string.Empty;
        var text = value.Trim();
        foreach (var marker in new[] { "{StaticResource ", "{DynamicResource " })
        {
            if (text.StartsWith(marker, StringComparison.Ordinal) && text.EndsWith('}'))
            {
                var inner = text[marker.Length..^1].Trim();
                if (inner.Length == 0 || inner.Contains('{') || inner.Contains('=')) return false;
                key = inner;
                return true;
            }
        }
        return false;
    }

    /// <summary>Parses a key-frame KeyTime: StaticResource keys resolve now
    /// when a scope is available, otherwise the key defers to storyboard run
    /// (template visual states, like other template deferrals).</summary>
    private static KeyTime ParseKeyTimeOrDefer(IKeyTimeDeferred frame, string value, IXmlLineInfo lineInfo, Func<string, object?>? resolveResource)
    {
        var text = value.Trim();
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                throw Fail(lineInfo, $"'{value}' takes a plain key (KeyTime={{StaticResource Key}}).");
            if (resolveResource?.Invoke(key) is KeyTime resolved) return resolved;
            if (resolveResource is not null)
                throw Fail(lineInfo, $"KeyTime '{{StaticResource {key}}}' was not found in the resource scope.");
            frame.DeferredKeyTimeKey = key;
            return KeyTime.Uniform;
        }
        return ParseKeyTime(value, lineInfo);
    }

    private static IEasingFunction ParseEasingFunctionRef(string value, Func<string, object?>? resolveResource, IXmlLineInfo lineInfo)
    {
        var text = value.Trim();
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                throw Fail(lineInfo, $"'{value}' takes a plain key (EasingFunction={{StaticResource Key}}).");
            if (resolveResource is null)
                throw Fail(lineInfo, $"'{value}' needs a resource dictionary scope (inline trigger storyboards stay a precise boundary).");
            var resolved = resolveResource(key)
                ?? throw Fail(lineInfo, $"EasingFunction '{{StaticResource {key}}}' was not found in the resource scope.");
            if (resolved is not IEasingFunction easing)
                throw Fail(lineInfo, $"EasingFunction '{{StaticResource {key}}}' is <{resolved.GetType().Name}>, not an easing function.");
            return easing;
        }
        throw Fail(lineInfo, $"'{value}' takes EasingFunction={{StaticResource Key}} (nested easing elements stay a precise boundary).");
    }

    private static void ApplyTimelineAttribute(Timeline timeline, string name, string value, IXmlLineInfo lineInfo, Func<string, object?>? resolveResource = null)
    {
        // Board-level targets: a Storyboard may carry TargetName/TargetProperty
        // (unqualified or Storyboard.-prefixed) that its child timelines inherit
        // when they carry no target of their own. Native-legal on the board only.
        if (timeline is Storyboard board && name is "TargetName" or "Storyboard.TargetName")
        { Storyboard.SetTargetName(board, value); return; }
        if (timeline is Storyboard && name is "TargetProperty" or "Storyboard.TargetProperty")
        { Storyboard.SetTargetProperty((object)timeline, value); return; }
        switch (name)
        {
            case "Duration":
                if (TryTimelineResourceKey(value, out var durationKey))
                {
                    if (resolveResource?.Invoke(durationKey) is Duration resolvedDuration) { timeline.Duration = resolvedDuration; return; }
                    if (resolveResource?.Invoke(durationKey) is TimeSpan resolvedSpan) { timeline.Duration = new Duration(resolvedSpan); return; }
                    if (resolveResource is not null)
                        throw Fail(lineInfo, $"Duration '{{StaticResource {durationKey}}}' was not found in the resource scope.");
                    timeline.DeferredDurationKey = durationKey;
                    return;
                }
                try { timeline.Duration = Duration.Parse(value); return; }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                { throw Fail(lineInfo, $"'{value}' is not a Duration (Automatic/Forever/0:0:0.5)."); }
            case "BeginTime":
                try { timeline.BeginTime = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); return; }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                { throw Fail(lineInfo, $"'{value}' is not a TimeSpan (0:0:0.5)."); }
            case "RepeatBehavior":
                try { timeline.RepeatBehavior = RepeatBehavior.Parse(value); return; }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
                { throw Fail(lineInfo, $"'{value}' is not a RepeatBehavior (Forever/3x/0:0:5)."); }
            case "AutoReverse":
                timeline.AutoReverse = ParseBool(value, lineInfo);
                return;
            case "FillBehavior":
                try { timeline.FillBehavior = Enum.Parse<FillBehavior>(value, ignoreCase: false); return; }
                catch (Exception ex) when (ex is ArgumentException or OverflowException)
                { throw Fail(lineInfo, $"'{value}' is not a FillBehavior (HoldEnd/Stop)."); }
            case "AccelerationRatio":
                try { timeline.AccelerationRatio = ParseDouble(value, lineInfo); return; }
                catch (ArgumentOutOfRangeException ex) { throw Fail(lineInfo, ex.Message); }
            case "DecelerationRatio":
                try { timeline.DecelerationRatio = ParseDouble(value, lineInfo); return; }
                catch (ArgumentOutOfRangeException ex) { throw Fail(lineInfo, ex.Message); }
        }
        if (timeline is DoubleAnimationUsingKeyFrames doubleKeys)
        {
            switch (name)
            {
                case "Storyboard.TargetName": Storyboard.SetTargetName(doubleKeys, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)doubleKeys, value); return;
            }
        }
        if (timeline is PointAnimationUsingKeyFrames pointKeys)
        {
            switch (name)
            {
                case "Storyboard.TargetName": Storyboard.SetTargetName(pointKeys, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)pointKeys, value); return;
            }
        }
        if (timeline is ObjectAnimationUsingKeyFrames objectKeys)
        {
            switch (name)
            {
                case "Storyboard.TargetName": Storyboard.SetTargetName(objectKeys, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)objectKeys, value); return;
            }
        }
        if (timeline is BooleanAnimationUsingKeyFrames booleanKeys)
        {
            switch (name)
            {
                case "Storyboard.TargetName": Storyboard.SetTargetName(booleanKeys, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)booleanKeys, value); return;
            }
        }
        if (timeline is ThicknessAnimationUsingKeyFrames thicknessKeys)
        {
            switch (name)
            {
                case "Storyboard.TargetName": Storyboard.SetTargetName(thicknessKeys, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)thicknessKeys, value); return;
            }
        }
        if (timeline is DoubleAnimation animation)
        {
            switch (name)
            {
                case "From": ParseDoubleAnimationEndpoint(animation, "From", value, lineInfo); return;
                case "To": ParseDoubleAnimationEndpoint(animation, "To", value, lineInfo); return;
                case "By": ParseDoubleAnimationEndpoint(animation, "By", value, lineInfo); return;
                case "Storyboard.TargetName": Storyboard.SetTargetName(animation, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)animation, value); return;
                case "EasingFunction":
                    animation.EasingFunction = ParseEasingFunctionRef(value, resolveResource, lineInfo);
                    return;
            }
        }
        if (timeline is ThicknessAnimation thickness)
        {
            switch (name)
            {
                case "From": thickness.From = ParseThickness(value, lineInfo); return;
                case "To": thickness.To = ParseThickness(value, lineInfo); return;
                case "By": thickness.By = ParseThickness(value, lineInfo); return;
                case "Storyboard.TargetName": Storyboard.SetTargetName(thickness, value); return;
                case "Storyboard.TargetProperty": Storyboard.SetTargetProperty((object)thickness, value); return;
                case "EasingFunction":
                    thickness.EasingFunction = ParseEasingFunctionRef(value, resolveResource, lineInfo);
                    return;
            }
        }
        throw Fail(lineInfo, $"'{name}' is not valid on <{timeline.GetType().Name}> (Duration/BeginTime/RepeatBehavior/AutoReverse/FillBehavior/AccelerationRatio/DecelerationRatio plus From/To/By and Storyboard.TargetName/TargetProperty on animations; Completed= wires a code-behind handler).");
    }

    /// <summary>Reads entry text (empty elements yield ""); nested elements are a precise error.</summary>
    private static string ReadEntryText(XmlReader reader, IXmlLineInfo lineInfo, string entry)
    {
        if (reader.IsEmptyElement) { reader.Read(); return string.Empty; }
        reader.Read(); // past the entry start tag
        SkipWhitespace(reader);
        var text = string.Empty;
        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
        {
            text = reader.Value;
            reader.Read();
            SkipWhitespace(reader);
        }
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{entry}> resource entry holds text only (no nested elements).");
        reader.Read(); // past the entry end tag
        return text;
    }

    private static void LoadSourceDictionary(ResourceDictionary dict, string source, IXmlLineInfo lineInfo, int depth)
    {
        PackUri.ResolvedPackUri parsed;
        try { parsed = PackUri.Parse(source); }
        catch (FormatException ex) { throw Fail(lineInfo, $"'{source}' is not a valid pack URI ({ex.Message})."); }
        // The WPF Fluent theme assembly never ships to the browser: the
        // engine carries the theme keys converted Fluent-era apps merge.
        if (PackUri.KeyFor(parsed) == PackUri.KeyFor(FluentTheme.PackUri))
        {
            dict.MergedDictionaries.Add(LoadResourceDictionary(FluentTheme.Xaml, depth));
            dict.Source = new Uri(source, UriKind.RelativeOrAbsolute);
            return;
        }
        var current = Application.Current;
        if (current is null)
            throw Fail(lineInfo, $"Cannot load '{source}': no Application.Current with a pack store is set.");
        string? xaml = null;
        try { xaml = current.GetPackResource(source); }
        catch (FormatException ex) { throw Fail(lineInfo, $"'{source}' is not a valid pack URI ({ex.Message})."); }
        // Converted assemblies embed every loose .xaml (the converter's
        // EmbeddedXaml): relative and same-assembly pack Sources resolve
        // through those manifest bytes; cross-assembly pack URIs consult only
        // the named assembly.
        xaml ??= WpfAppBoot.FindPackText(parsed.Path, parsed.Assembly);
        if (xaml is null)
        {
            // Compiled BAML-only dictionaries (the HandyControl lib theme:
            // SkinDefault/Theme/Colors ship as .baml with no loose .xaml).
            var baml = WpfAppBoot.FindPackBytes(System.IO.Path.ChangeExtension(parsed.Path, ".baml")!, parsed.Assembly);
            if (baml is null)
                throw Fail(lineInfo, $"No pack resource registered for '{source}' (Application.RegisterPackResource).");
            try { dict.MergedDictionaries.Add(LoadBamlDictionary(baml, lineInfo, depth)); }
            catch (XamlLoadException ex)
            {
                // Name the merge Source like the loose path names its file:
                // raw BAML offsets alone never say which dictionary failed.
                throw Fail(lineInfo, $"BAML dictionary '{source}' failed to load ({ex.Message}).");
            }
            dict.Source = new Uri(source, UriKind.RelativeOrAbsolute);
            return;
        }
        try { dict.MergedDictionaries.Add(LoadResourceDictionary(xaml, depth)); }
        catch (XamlLoadException ex)
        {
            // Name the merge Source like the BAML path names its file:
            // raw offsets alone never say which dictionary failed.
            throw Fail(lineInfo, $"Resource dictionary '{source}' failed to load ({ex.Message}).");
        }
        dict.Source = new Uri(source, UriKind.RelativeOrAbsolute);
    }

    /// <summary>
    /// Loads a compiled-BAML ResourceDictionary and resolves its own Source=
    /// merge (the skin merge roots carry one), mirroring
    /// <see cref="ReadResourceDictionaryRoot"/> with the same nesting guard.
    /// </summary>
    internal static ResourceDictionary LoadBamlDictionary(byte[] bytes, IXmlLineInfo lineInfo, int depth)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(lineInfo);
        if (depth > 8)
            throw Fail(lineInfo, "ResourceDictionary nesting is too deep (possible Source= cycle).");
        var dictionary = BamlReader.LoadDictionary(bytes);
        if (dictionary.Source?.OriginalString is { Length: > 0 } nested)
            LoadSourceDictionary(dictionary, nested, lineInfo, depth + 1);
        return dictionary;
    }

    /// <summary>
    /// Stores a gallery AutomationProperties attached value (attribute and
    /// property-element forms share this path, so code Get/Set and XAML
    /// read one backing). Bound values stay a precise boundary: bindings
    /// on attached properties are rejected in ApplyMarkupExtension.
    /// </summary>
    private static void ApplyAutomationProperty(UIElement element, string property, string value, IXmlLineInfo lineInfo)
    {
        switch (property)
        {
            case "Name":
            case "HelpText":
            case "AutomationId":
                element.SetAttached("AutomationProperties." + property, value);
                return;
            case "HeadingLevel":
                if (!Enum.TryParse<AutomationHeadingLevel>(value.Trim(), ignoreCase: true, out var heading))
                    throw Fail(lineInfo, $"'{value}' is not an AutomationHeadingLevel (None/Level1..Level9).");
                element.SetAttached("AutomationProperties.HeadingLevel", heading);
                return;
            default: throw Fail(lineInfo, $"'AutomationProperties.{property}' is not supported (Name/HelpText/AutomationId/HeadingLevel).");
        }
    }

    /// <summary>
    /// Reads an &lt;AutomationProperties.Name&gt;text&lt;/...&gt; property
    /// element (gallery HeaderTile/IconDataField pattern). Nested elements
    /// (MultiBinding) stay a precise boundary.
    /// </summary>
    private static void ReadAutomationPropertyElement(UIElement element, string property, XmlReader reader, IXmlLineInfo lineInfo)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            ApplyAutomationProperty(element, property, string.Empty, lineInfo);
            return;
        }
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType == XmlNodeType.Element)
        {
            if (reader.LocalName == "MultiBinding" && property is "Name" or "HelpText")
            {
                ReadAutomationMultiBinding(element, property, reader, lineInfo);
                return;
            }
            throw Fail(lineInfo, $"<AutomationProperties.{property}> takes text only ('{reader.LocalName}' needs data-binding support).");
        }
        var value = string.Empty;
        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
        {
            value = reader.Value;
            reader.Read();
            SkipWhitespace(reader);
        }
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"Malformed <AutomationProperties.{property}>.");
        reader.Read(); // past the property end tag
        ApplyAutomationProperty(element, property, value, lineInfo);
    }

    /// <summary>
    /// Reads an AutomationProperties MultiBinding (gallery HeaderTile pattern):
    /// child bindings resolve their own sources and combine through StringFormat
    /// (or a resource converter key resolved at apply time). Evaluation waits for
    /// Loaded so FindAncestor sources exist on the live tree; an immediate attempt
    /// covers trees that are already connected.
    /// </summary>
    private static void ReadAutomationMultiBinding(UIElement element, string property, XmlReader reader, IXmlLineInfo lineInfo)
    {
        var multi = new MultiBinding();
        if (reader.GetAttribute("StringFormat") is { } format) multi.StringFormat = format;
        if (reader.GetAttribute("Mode") is { } mode) multi.Mode = ParseMultiBindingMode(mode, lineInfo);
        if (reader.GetAttribute("Converter") is { } converter && converter.Length > 0)
        {
            if (!converter.StartsWith("{StaticResource ", StringComparison.Ordinal) || !converter.EndsWith('}'))
                throw Fail(lineInfo, $"MultiBinding Converter= takes a plain {{StaticResource Key}} ('{converter}').");
            multi.DeferredConverterKey = converter["{StaticResource ".Length..^1].Trim();
        }
        if (reader.GetAttribute("ConverterParameter") is { } parameter) multi.ConverterParameter = parameter;
        if (reader.IsEmptyElement)
            throw Fail(lineInfo, $"<AutomationProperties.{property}> MultiBinding needs at least one <Binding>.");
        reader.Read(); // past the MultiBinding start tag
        for (SkipWhitespace(reader); reader.NodeType != XmlNodeType.EndElement; SkipWhitespace(reader))
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Binding")
                throw Fail(lineInfo, $"<AutomationProperties.{property}> MultiBinding takes <Binding> children only ('{reader.LocalName}').");
            multi.Bindings.Add(ReadMultiBindingChild(reader, lineInfo));
        }
        reader.Read(); // past the MultiBinding end tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"Malformed <AutomationProperties.{property}>.");
        reader.Read(); // past the property end tag
        if (element is FrameworkElement fe)
        {
            var attachedName = "AutomationProperties." + property;
            fe.Loaded += (_, _) =>
            {
                if (multi.Evaluate(fe) is { } combined)
                    fe.SetAttached(attachedName, combined?.ToString() ?? string.Empty);
            };
            if (multi.Evaluate(fe) is { } immediate)
                fe.SetAttached(attachedName, immediate?.ToString() ?? string.Empty);
        }
    }

    private static Binding ReadMultiBindingChild(XmlReader reader, IXmlLineInfo lineInfo)
    {
        var child = new Binding();
        if (reader.GetAttribute("Path") is { } path) child.Path = path;
        if (reader.GetAttribute("XPath") is not null)
            throw Fail(lineInfo, "MultiBinding <Binding> XPath= stays a precise boundary (XML sources are out of scope).");
        if (reader.GetAttribute("Mode") is { } mode) child.Mode = ParseMultiBindingMode(mode, lineInfo);
        if (reader.GetAttribute("RelativeSource") is { } relative)
            child.RelativeSource = ParseRelativeSource(relative, lineInfo);
        if (reader.GetAttribute("ElementName") is not null)
            throw Fail(lineInfo, "MultiBinding <Binding> ElementName= stays a precise boundary (namescope lookup at apply time).");
        if (reader.GetAttribute("Source") is { } source && source.Length > 0)
            throw Fail(lineInfo, "MultiBinding <Binding> Source= stays a precise boundary (inline sources only).");
        if (reader.GetAttribute("Converter") is { } converter && converter.Length > 0)
            throw Fail(lineInfo, "MultiBinding <Binding> Converter= stays a precise boundary (converters live on the MultiBinding).");
        if (reader.GetAttribute("StringFormat") is { } format) child.StringFormat = format;
        if (reader.GetAttribute("FallbackValue") is { } fallback) child.FallbackValue = fallback;
        if (reader.GetAttribute("TargetNullValue") is { } targetNull) child.TargetNullValue = targetNull;
        if (reader.GetAttribute("UpdateSourceTrigger") is not null)
            throw Fail(lineInfo, "MultiBinding <Binding> UpdateSourceTrigger= stays a precise boundary (one-way composite).");
        if (!reader.IsEmptyElement)
        {
            reader.Read();
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, "MultiBinding <Binding> takes attributes only (nested content stays a boundary).");
        }
        reader.Read(); // past the Binding tag
        return child;
    }

    private static BindingMode ParseMultiBindingMode(string value, IXmlLineInfo lineInfo) =>
        value switch
        {
            "OneWay" => BindingMode.OneWay,
            "OneTime" => BindingMode.OneTime,
            "TwoWay" => BindingMode.TwoWay,
            "Default" => BindingMode.Default,
            _ => throw Fail(lineInfo, $"'{value}' is not a MultiBinding mode (OneWay/OneTime/TwoWay/Default)."),
        };

    /// <summary>
    /// Assigns a nested element read from property-element syntax
    /// (&lt;HeaderTile.Source&gt;&lt;Image/&gt;...): plain CLR property first,
    /// then a same-named dependency-property field. Engine types keep their
    /// explicit tables; this covers converted-app controls.
    /// </summary>
    private static bool TrySetPropertyElementValue(UIElement element, string property, object? value, IXmlLineInfo lineInfo)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var prop = element.GetType().GetProperty(property, flags);
        if (prop is not null && prop.CanWrite && prop.SetMethod?.IsPublic == true
            && (value is null || prop.PropertyType.IsInstanceOfType(value)))
        {
            prop.SetValue(element, value);
            if (value is UIElement visual && element is FrameworkElement fe && visual.Parent is null) fe.AttachVisualChild(visual);
            return true;
        }
        if (element.GetType().GetField(property + "Property",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null)
                is DependencyProperty dp
            && (value is null || dp.PropertyType.IsInstanceOfType(value)))
        {
            element.SetValue(dp, value);
            if (value is UIElement visual2 && element is FrameworkElement fe2 && visual2.Parent is null) fe2.AttachVisualChild(visual2);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Converted-app content property (matches [ContentProperty]): routes
    /// direct children and inner text into the named CLR slot (gallery
    /// ControlExample.ExampleContent). Engine types keep their explicit
    /// tables above; this only runs for types those tables reject.
    /// </summary>
    private static System.Reflection.PropertyInfo? FindContentProperty(UIElement element)
    {
        var attr = (ContentPropertyAttribute?)System.Attribute.GetCustomAttribute(
            element.GetType(), typeof(ContentPropertyAttribute), inherit: true);
        if (attr is null || string.IsNullOrEmpty(attr.Name)) return null;
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        return element.GetType().GetProperty(attr.Name, flags);
    }

    private static bool ContentSlotFree(object? current) =>
        current is null || (current is string s && s.Length == 0);

    /// <summary>
    /// Whether a ContentControl comes from a converted image (its theme ships
    /// BAML-only behind the SkinDefault stop, so content parents into the
    /// logical tree through [ContentProperty] without theme visuals; engine
    /// types keep their explicit tables above).
    /// </summary>
    private static bool IsConvertedContentControl(ContentControl cc) =>
        !ReferenceEquals(cc.GetType().Assembly, typeof(ContentControl).Assembly);

    internal static bool TryAttachCustomContent(UIElement element, UIElement child, IXmlLineInfo lineInfo)
    {
        var prop = FindContentProperty(element);
        if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return false;
        if (!prop.PropertyType.IsInstanceOfType(child)) return false;
        if (!ContentSlotFree(prop.GetValue(element))) return false;
        prop.SetValue(element, child);
        if (element is FrameworkElement fe && child.Parent is null) fe.AttachVisualChild(child);
        // Converted default templates (CardDemo hc:Card) mirror ContentSource
        // presenters: content arriving through [ContentProperty] after the
        // template applied needs a refresh, or the middle presenter keeps
        // mirroring null (plain-CLR sets raise no refresh on their own).
        if (element is Control control && control.ActiveTemplate is not null)
            TemplateApplier.RefreshTemplate(control);
        return true;
    }

    private static bool TryAttachCustomText(UIElement element, string text, IXmlLineInfo lineInfo)
    {
        var prop = FindContentProperty(element);
        if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return false;
        if (prop.PropertyType != typeof(string) && prop.PropertyType != typeof(object)) return false;
        if (!ContentSlotFree(prop.GetValue(element))) return false;
        prop.SetValue(element, text.Trim());
        return true;
    }

    private static bool TrySetCustomTextProperty(UIElement element, string property, string value, IXmlLineInfo lineInfo)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var prop = element.GetType().GetProperty(property, flags);
        if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return false;
        if (prop.PropertyType == typeof(string) || prop.PropertyType == typeof(object))
        {
            prop.SetValue(element, value);
            return true;
        }
        try
        {
            var converted = StyleValueConverter.Convert(value, prop.PropertyType);
            if (converted is null) return false;
            prop.SetValue(element, converted);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Last-resort parity for engine controls: plain CLR properties the
    /// per-type tables do not name (Control.IsTabStop, TextBox.IsReadOnly).
    /// Explicit tables and their precise rejections run first.
    /// </summary>
    private static bool TrySetPlainClrProperty(UIElement element, string local, string value, IXmlLineInfo lineInfo)
    {
    const System.Reflection.BindingFlags flags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
    var prop = element.GetType().GetProperty(local, flags);
    if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return false;
    if (prop.PropertyType == typeof(string) || prop.PropertyType == typeof(object))
    {
        prop.SetValue(element, value);
        return true;
    }
    try
    {
        var converted = StyleValueConverter.Convert(value, prop.PropertyType);
        if (converted is null || !prop.PropertyType.IsInstanceOfType(converted)) return false;
        prop.SetValue(element, converted);
        return true;
    }
    catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
    {
        return false;
    }
    }

    private static void SetPropertyElement(UIElement element, string property, string value, IXmlLineInfo lineInfo, List<PendingStatic> pending)
    {
        // Property-element value form (<TextBlock.Text>{StaticResource K}</...>):
        // StaticResource defers like attributes; anything else keeps the
        // legacy literal behavior.
        if (value.StartsWith('{') && value.EndsWith('}') && value.Length > 2)
        {
            var head = value[1..^1].Trim().Split([' ', '\t', ','], 2)[0];
            if (head == "StaticResource")
            {
                pending.Add(new PendingStatic(element, null, property,
                    ParseStaticResourceKey(value[1..^1].Trim(), value, lineInfo)));
                return;
            }
            if (head == "DynamicResource")
            {
                if (element is not FrameworkElement dynamicTarget)
                    throw Fail(lineInfo, $"Cannot apply DynamicResource '{property}' on <{element.GetType().Name}>.");
                var key = value[1..^1].Trim()["DynamicResource".Length..].Trim();
                if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                    if (key.Length == 0 || key.Contains('='))
                        throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
                    if (key.StartsWith("{x:Static", StringComparison.Ordinal))
                    {
                        if (!key.EndsWith("}"))
                            throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
                        var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), value, lineInfo);
                        if (nested is ResourceKey attachedKey &&
                            SystemTheme.TryFindSystemResource(attachedKey, out var attachedSystem) && attachedSystem is not null &&
                            TrySetPropertyElementValue(element, property, attachedSystem, lineInfo)) return;
                        if (nested is not ResourceKey)
                            throw Fail(lineInfo, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                        key = nested.ToString() ?? string.Empty;
                        if (key.Length == 0)
                            throw Fail(lineInfo, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                    }
                    else if (key.Contains('{'))
                        throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
                pending.Add(new PendingStatic(dynamicTarget, null, property, key) { IsDynamic = true });
                return;
            }
        }
        switch (element)
        {
            case TabItem tabItemHeader when property == "Header": tabItemHeader.Header = value; return;
            case TabItem tabItemContent when property == "Content": tabItemContent.Content = value; return;
            case ToolTip toolTipContent when property == "Content": toolTipContent.Content = value; return;
            case Button b when property == "Content": b.Content = value; return;
            case ToggleButton tb when property == "Content": tb.Content = value; return;
            case ListBoxItem li when property == "Content": li.Content = value; return;
            case ComboBoxItem ci when property == "Content": ci.Content = value; return;
            case HeaderedItemsControl h when property == "Header": h.Header = value; return;
            case TreeViewItem ti when property == "Header": ti.Header = value; return;
            case MenuItem mi when property == "Header": mi.Header = value; return;
            case TextBlock t when property == "Text": t.Text = value; return;
            case TextBox tb when property == "Text": tb.Text = value; return;
            case Window w when property == "Title": w.Title = value; return;
            case Expander ex when property == "Content": ex.Content = value; return;
            case Expander ex when property == "Header": ex.Header = value; return;
            default:
                if (TrySetCustomTextProperty(element, property, value, lineInfo)) return;
                    throw Fail(lineInfo, $"<{element.GetType().Name}.{property}> is not a settable content property.");
        }
    }

    private static void FinishTextContent(UIElement element, string text, IXmlLineInfo lineInfo)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        switch (element)
        {
            case Span s when string.IsNullOrEmpty(s.Text): s.Text = text.Trim(); return;
            case Span s: s.Text += StripLeadingBoundaryWhitespace(text); return;
            case Button b when b.Content is null or "": b.Content = text.Trim(); return;
            case ToggleButton tb when tb.Content is null or "": tb.Content = text.Trim(); return;
            case Label label when label.Content is null or "": label.Content = text.Trim(); return;
            case ListBoxItem li when string.IsNullOrEmpty(li.Content): li.Content = text.Trim(); return;
            case ComboBoxItem ci when string.IsNullOrEmpty(ci.Content): ci.Content = text.Trim(); return;
            case ItemsControl ic when ic is not TreeViewItem: ic.AddItem(text.Trim()); return;
            case TreeViewItem ti when ti.Header is null || ti.Header is string hs && hs.Length == 0: ti.Header = text.Trim(); return;
            case MenuItem mi when mi.Header is null || mi.Header is string ms && ms.Length == 0: mi.Header = text.Trim(); return;
            case TextBlock t when string.IsNullOrEmpty(t.Text): t.AppendLiteral(text.Trim()); return;
            // Inline-first mixed content (Span/Run/LineBreak plus direct text):
            // direct chunks fold after the inlines with boundary whitespace
            // stripped. Leading direct text would reorder (folding limit).
            case TextBlock t:
                t.AppendLiteral(StripLeadingBoundaryWhitespace(text));
                t.TrimTrailingLiteralWhitespace();
                return;
            case TextBox tb when string.IsNullOrEmpty(tb.Text): tb.Text = text.Trim(); return;
            case ToolTip toolTipText when toolTipText.Content is null: toolTipText.Content = text.Trim(); return;
            case Paragraph pg: pg.Inlines.Add(text.Trim()); return;

            default:
                if (TryAttachCustomText(element, text, lineInfo)) return;
                    throw Fail(lineInfo, $"<{element.GetType().Name}> cannot hold inner text.");
        }
    }

    /// <summary>Strips element-boundary indentation (a leading whitespace run holding a newline).</summary>
    private static string StripLeadingBoundaryWhitespace(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index > 0 && text[..index].Contains('\n') ? text[index..] : text;
    }

    private static void AddContentChild(UIElement element, UIElement child, IXmlLineInfo lineInfo, List<PendingStatic> pending, List<FrameworkElement> scope, List<XamlEventWire> wires)
    {
        // Attach first, then flush against the lexical ancestor scopes (the
        // intermediate Parent links may not exist yet).
        var attached = TryAttachContentChild(element, child, lineInfo);
        if (attached) FlushPending(child, pending, lineInfo, scope, wires);
    }

    private static bool TryAttachContentChild(UIElement element, UIElement child, IXmlLineInfo lineInfo)
    {
        switch (element)
        {
            case Window w when w.Content is null: w.Content = child; return true;
            case Page pg when pg.Content is null: pg.Content = child; return true;
            case Frame fr when fr.Content is null: fr.Content = child; return true;
            case ScrollViewer sv when sv.Content is null: sv.Content = child; return true;
            case Viewbox vb when vb.Child is null: vb.Child = child; return true;
            case Viewbox: throw Fail(lineInfo, "<Viewbox> accepts a single child element.");
            case TextBlock tb when child is Span span: tb.AddInline(span); return true;
            case Span host when child is Span nested && nested is not Hyperlink: host.Text += nested.Text; return true;
            case Paragraph pg when child is Inline inline: pg.Inlines.Add(inline); return true;

            case Hyperlink hl when child is Inline inline: hl.Inlines.Add(inline); return true;

            case FlowDocument fd when child is Block block: fd.Blocks.Add(block); return true;

            case FlowDocumentScrollViewer vs when vs.Document is null && child is FlowDocument doc: vs.Document = doc; return true;
            case RichTextBox rb when child is FlowDocument rdoc && rb.PlainText.Length == 0: rb.Document = rdoc; return true;

            case InkCanvas ink: ink.Children.Add(child); return true;

            case ListBox lb when child is ListBoxItem: lb.AddChild(child); return true;
            case ListBox: throw Fail(lineInfo, "<ListBox> children must be <ListBoxItem>.");
            case DataGrid: throw Fail(lineInfo, "<DataGrid> takes column definitions in <DataGrid.Columns>; rows are data, set in code.");
            case TreeView tv when child is TreeViewItem: tv.AddChild(child); return true;
            case TreeView: throw Fail(lineInfo, "<TreeView> children must be <TreeViewItem>.");
            case TreeViewItem ti when child is TreeViewItem: ti.AddChild(child); return true;
            case TreeViewItem: throw Fail(lineInfo, "<TreeViewItem> children must be <TreeViewItem>.");
            case Menu m when child is MenuItem or Separator: m.AddChild(child); return true;
            case Menu: throw Fail(lineInfo, "<Menu> children must be <MenuItem>.");
            case MenuItem mi when child is MenuItem or Separator: mi.AddChild(child); return true;
            case MenuItem: throw Fail(lineInfo, "<MenuItem> children must be <MenuItem>.");
            case ContextMenu cm when child is MenuItem menuRow: cm.AddItem(menuRow); return true;
            case ContextMenu: throw Fail(lineInfo, "<ContextMenu> children must be <MenuItem>.");
            case ItemsControl ic: ic.AddItem(child); return true;
            case Border b when b.LogicalChildren.Count == 0: b.AddChild(child); return true;
            case Decorator d when d.Child is null: d.Child = child; return true;
            case Decorator: throw Fail(lineInfo, "<Decorator> accepts a single child element.");
            case Popup pp when pp.Child is null: pp.Child = child; return true;
            case Popup: throw Fail(lineInfo, "<Popup> accepts a single child element.");
            case Expander ex when ex.Content is null || ex.Content is string exText && exText.Length == 0:
                ex.Content = child;
                return true;
            case ContentControl cc when IsConvertedContentControl(cc) && TryAttachCustomContent(element, child, lineInfo):
                return true;
            case ContentControl cc when cc.Content is null || cc.Content is string ccText && ccText.Length == 0:
                cc.Content = child;
                return true;
            case FrameworkElement fe when element is Panel: fe.AddChild(child); return true;
            default:
                if (TryAttachCustomContent(element, child, lineInfo)) return true;
                    throw Fail(lineInfo, $"<{element.GetType().Name}> accepts no (further) child elements (got <{child.GetType().Name}>).");
        }
    }

    private static void ApplyAttribute(UIElement element, XmlReader reader, IXmlLineInfo lineInfo, Dictionary<string, object> names, List<PendingStatic> pending, List<XamlEventWire> wires)
    {
        var local = reader.LocalName;
        var value = reader.Value;

        // Designer namespaces carry no runtime semantics: mc:Ignorable="d"
        // plus d:DesignHeight/d:DesignWidth on real-world roots.
        // presentationOptions:Freeze is a compile-time hint with no runtime
        // behavior; xml:space is handled by the XML reader itself.
        if (reader.Prefix is "d" or "mc" or "presentationOptions") return;

        // x:Class wires codebehind, which has no browser equivalent; the
        // namescope (x:Name/Name) is what the engine keeps.
        if ((reader.Prefix == "x" && local is "Name" or "Key" or "Class") || (reader.Prefix == string.Empty && local == "Name"))
        {
            if (local is "Key" or "Class") return; // accepted, no runtime semantics yet.
            if (names.ContainsKey(value)) throw Fail(lineInfo, $"Duplicate x:Name '{value}'.");
            names[value] = element;
            if (element is FrameworkElement named) named.Name = value;
            return;
        }

        // Markup extensions: {Binding ...} applies immediately; {StaticResource ...}
        // defers to attach time so ancestor resource scopes are visible.
        if (value.StartsWith('{'))
        {
            ApplyMarkupExtension(element, local, value, lineInfo, pending, prefix => reader.LookupNamespace(prefix));
            return;
        }

        // Codebehind event wiring (converted apps bind handlers at boot):
        // record now, bind later against the component instance.
        if (local == "Click" && (element is ButtonBase || element is MenuItem || element is Hyperlink))
        {
            wires.Add(new XamlEventWire(element, "Click", value));
            return;
        }
        if (local == "RequestNavigate" && element is Hyperlink)
        {
            wires.Add(new XamlEventWire(element, "RequestNavigate", value));
            return;
        }
        if (local == "PasswordChanged" && element is PasswordBox)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local is "Checked" or "Unchecked" && element is ToggleButton)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local is "GotKeyboardFocus" or "LostKeyboardFocus" && element is UIElement)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local == "ContentRendered" && element is Frame)
        {
            wires.Add(new XamlEventWire(element, "ContentRendered", value));
            return;
        }
        if (local == "Navigated" && element is Frame)
        {
            wires.Add(new XamlEventWire(element, "Navigated", value));
            return;
        }
        if (local == "SelectionChanged" && element is Selector)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local == "SelectionMode" && element is ListBox modeBox)
        {
            // The engine ListBox is not a Selector (custom items/groups
            // machinery), so the shared Selector branch cannot land this;
            // SelectionMode is retained with a Single default.
            if (!Enum.TryParse<SelectionMode>(value, ignoreCase: true, out var listMode))
                throw Fail(lineInfo, $"'{value}' is not a SelectionMode (Single/Multiple/Extended).");
            modeBox.SelectionMode = listMode;
            return;
        }
        if (local == "ValueChanged" && element is RangeBase)
        {
            wires.Add(new XamlEventWire(element, "ValueChanged", value));
            return;
        }
        if (local is "DragDelta" or "DragStarted" or "DragCompleted" && element is Thumb)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local is "Opened" or "Closed" && element is Popup or ToolTip)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        if (local == "Loaded" && element is FrameworkElement)
        {
            wires.Add(new XamlEventWire(element, "Loaded", value));
            return;
        }
        if (local == "PreviewKeyDown" && element is FrameworkElement)
        {
            wires.Add(new XamlEventWire(element, "PreviewKeyDown", value));
            return;
        }
        if (local == "PreviewMouseLeftButtonUp" && element is FrameworkElement)
        {
            wires.Add(new XamlEventWire(element, "PreviewMouseLeftButtonUp", value));
            return;
        }
        if (local == "PreviewMouseDown" && element is FrameworkElement)

        {

            wires.Add(new XamlEventWire(element, "PreviewMouseDown", value));

            return;

        }

        if (local == "PreviewMouseUp" && element is FrameworkElement)

        {

            wires.Add(new XamlEventWire(element, "PreviewMouseUp", value));

            return;

        }

        if (local == "StrokeCollected" && element is InkCanvas)

        {

            wires.Add(new XamlEventWire(element, "StrokeCollected", value));

            return;

        }

        if (local == "PreviewStylusButtonDown" && element is FrameworkElement)

        {

            wires.Add(new XamlEventWire(element, "PreviewStylusButtonDown", value));

            return;

        }

        if (local == "SizeChanged" && element is FrameworkElement)
        {
            wires.Add(new XamlEventWire(element, "SizeChanged", value));
            return;
        }
        if (local == "ScrollChanged" && element is ScrollViewer)
        {
            wires.Add(new XamlEventWire(element, "ScrollChanged", value));
            return;
        }
        if (local is "Drop" or "DragEnter" or "DragOver" or "DragLeave" or "PreviewDrop" or "PreviewDragEnter" or "PreviewDragOver" or "PreviewDragLeave" && element is UIElement)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }
        // Converted custom events (the PC Space Map TreemapControl
        // NodeSelected/ZoomRequested): any public instance event wires by
        // name here; Hydrate binds it to the codebehind handler. Properties
        // never match GetEvent, so plain attributes fall through untouched.
        if (element.GetType().GetEvent(local, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is not null)
        {
            wires.Add(new XamlEventWire(element, local, value));
            return;
        }

        ApplyPlainAttribute(element, local, value, lineInfo);
    }

    /// <summary>
    /// Shared Control/FrameworkElement attributes for the tab/progress/tooltip
    /// slice (the decompiled maze below only knows older types, so the new
    /// elements land here first; unmatched names fall through to it).
    /// </summary>
    private static bool ApplyControlChrome(Control control, string local, string value, IXmlLineInfo lineInfo)
    {
        switch (local)
        {
            case "Background": control.Background = ParseBrush(value, lineInfo); return true;
            case "Foreground": control.Foreground = ParseBrush(value, lineInfo); return true;
            case "BorderBrush": control.BorderBrush = ParseBrush(value, lineInfo); return true;
            case "BorderThickness": control.BorderThickness = ParseThickness(value, lineInfo); return true;
            case "Padding": control.Padding = ParseThickness(value, lineInfo); return true;
            case "FontFamily": control.FontFamily = new FontFamily(value); return true;
            case "FontSize": control.FontSize = ParseDouble(value, lineInfo); return true;
            case "FontWeight": control.FontWeight = ParseFontWeight(value, lineInfo); return true;
            case "FontStyle": control.FontStyle = ParseFontStyle(value, lineInfo); return true;
            case "HorizontalContentAlignment": control.HorizontalContentAlignment = ParseHAlign(value, lineInfo); return true;
            case "VerticalContentAlignment": control.VerticalContentAlignment = ParseVAlign(value, lineInfo); return true;
        }
        if (control is not FrameworkElement fe) return false;
        switch (local)
        {
            case "Width": fe.Width = ParseLength(value, lineInfo); return true;
            case "Height": fe.Height = ParseLength(value, lineInfo); return true;
            case "HorizontalAlignment": fe.HorizontalAlignment = ParseHAlign(value, lineInfo); return true;
            case "VerticalAlignment": fe.VerticalAlignment = ParseVAlign(value, lineInfo); return true;
            case "Margin": fe.Margin = ParseThickness(value, lineInfo); return true;
            case "ToolTip": fe.ToolTip = value; ToolTipService.EnsureWired(fe); return true;
            case "Visibility": fe.Visibility = ParseVisibility(value, lineInfo); return true;
            case "IsHitTestVisible": fe.IsHitTestVisible = ParseBool(value, lineInfo); return true;
            case "MaxWidth": fe.MaxWidth = ParseDouble(value, lineInfo); return true;
            case "MaxHeight": fe.MaxHeight = ParseDouble(value, lineInfo); return true;
            case "MinWidth": fe.MinWidth = ParseDouble(value, lineInfo); return true;
            case "MinHeight": fe.MinHeight = ParseDouble(value, lineInfo); return true;
            case "ClipToBounds": fe.ClipToBounds = ParseBool(value, lineInfo); return true;
            case "Cursor": fe.Cursor = StyleValueConverter.ParseCursor(value); return true;
            case "Focusable": fe.Focusable = ParseBool(value, lineInfo); return true;
            case "AllowDrop": fe.AllowDrop = ParseBool(value, lineInfo); return true;
            case "SnapsToDevicePixels": fe.SnapsToDevicePixels = ParseBool(value, lineInfo); return true;
            case "OverridesDefaultStyle": fe.OverridesDefaultStyle = ParseBool(value, lineInfo); return true;
            case "IsEnabled": fe.IsEnabled = ParseBool(value, lineInfo); return true;
        }
        return false;
    }

    /// <summary>Shared Selector attributes (ComboBox falls through to these).</summary>
    private static bool ApplySelectorAttribute(Selector selector, string local, string value, IXmlLineInfo lineInfo)
    {
        if (local == "SelectedIndex") { selector.SelectedIndex = ParseInt(value, lineInfo); return true; }
        if (local == "SelectedValue") { selector.SelectedValue = value; return true; }
        if (local == "SelectedValuePath") { selector.SelectedValuePath = value; return true; }
        if (local == "IsSynchronizedWithCurrentItem") { selector.IsSynchronizedWithCurrentItem = ParseBool(value, lineInfo); return true; }
        if (local == "SelectionMode")
        {
            if (!Enum.TryParse<SelectionMode>(value, ignoreCase: true, out var mode))
                throw Fail(lineInfo, $"'{value}' is not a SelectionMode (Single/Multiple/Extended).");
            selector.SelectionMode = mode; return true;
        }
        return false;
    }


    /// <summary>
    /// Sets a converted-app control property by name (plain CLR property
    /// first, then a same-named dependency-property field). Converted
    /// controls (calculator displays, gallery tiles) expose their bindable
    /// surface this way; engine types keep their explicit parse tables.
    /// </summary>
    /// <summary>Shared ink/document attributes (InkCanvas, FlowDocument family).</summary>

    private static bool ApplyInkDocumentAttribute(UIElement element, string local, string value, IXmlLineInfo lineInfo)

    {

        if (element is TextElement textElement)

        {

            switch (local)

            {

            case "FontFamily":

                textElement.FontFamily = new FontFamily(value);

                return true;

            case "FontSize":

                textElement.FontSize = ParseDouble(value, lineInfo);

                return true;

            case "FontWeight":

                textElement.FontWeight = ParseFontWeight(value, lineInfo);

                return true;

            case "FontStyle":

                textElement.FontStyle = ParseFontStyle(value, lineInfo);

                return true;

            case "Foreground":

                textElement.Foreground = ParseBrush(value, lineInfo);

                return true;

            case "Background":

                textElement.Background = ParseBrush(value, lineInfo);

                return true;

            }

        }

        if (element is Block block)

        {

            switch (local)

            {

            case "TextAlignment":

                block.TextAlignment = ParseTextAlignment(value, lineInfo);

                return true;

            case "FlowDirection":

                block.FlowDirection = ParseFlowDirection(value, lineInfo);

                return true;

            }

        }

        if (element is Paragraph paragraph)

        {

            switch (local)

            {

            case "KeepTogether":

                paragraph.KeepTogether = ParseBool(value, lineInfo);

                return true;

            case "KeepWithNext":

                paragraph.KeepWithNext = ParseBool(value, lineInfo);

                return true;

            }

        }

        if (element is Run run && local == "Text")

        {

            run.Text = value;

            return true;

        }

        if (element is Hyperlink link && local == "NavigateUri")

        {

            try
            {
                link.NavigateUri = new Uri(value, UriKind.RelativeOrAbsolute);
            }
            catch (UriFormatException)
            {
                throw Fail(lineInfo, "'" + value + "' is not a valid NavigateUri.");
            }

            return true;

        }

        if (element is FlowDocument document)

        {

            switch (local)

            {

            case "FontFamily":

                document.FontFamily = new FontFamily(value);

                return true;

            case "FontSize":

                document.FontSize = ParseDouble(value, lineInfo);

                return true;

            case "FontWeight":

                document.FontWeight = ParseFontWeight(value, lineInfo);

                return true;

            case "FontStyle":

                document.FontStyle = ParseFontStyle(value, lineInfo);

                return true;

            case "Foreground":

                document.Foreground = ParseBrush(value, lineInfo);

                return true;

            case "Background":

                document.Background = ParseBrush(value, lineInfo);

                return true;

            case "TextAlignment":

                document.TextAlignment = ParseTextAlignment(value, lineInfo);

                return true;

            case "FlowDirection":

                document.FlowDirection = ParseFlowDirection(value, lineInfo);

                return true;

            case "PagePadding":

                document.PagePadding = ParseThickness(value, lineInfo);

                return true;

            }

        }

        if (element is FlowDocumentScrollViewer viewer)

        {

            switch (local)

            {

            case "Zoom":

                viewer.Zoom = ParseDouble(value, lineInfo);

                return true;

            case "HorizontalScrollBarVisibility":

                viewer.HorizontalScrollBarVisibility = ParseScrollBarVisibility(value, lineInfo);

                return true;

            case "VerticalScrollBarVisibility":

                viewer.VerticalScrollBarVisibility = ParseScrollBarVisibility(value, lineInfo);

                return true;

            }

        }

        if (element is InkCanvas ink)

        {

            switch (local)

            {

            case "EditingMode":

                if (!Enum.TryParse<InkCanvasEditingMode>(value, ignoreCase: true, out var mode))

                    throw Fail(lineInfo, "'" + value + "' is not an InkCanvasEditingMode (None/Ink/GestureOnly/InkAndGesture/Select/EraseByPoint/EraseByStroke).");

                ink.EditingMode = mode;

                return true;

            case "EditingModeInverted":

                if (!Enum.TryParse<InkCanvasEditingMode>(value, ignoreCase: true, out var inverted))

                    throw Fail(lineInfo, "'" + value + "' is not an InkCanvasEditingMode (None/Ink/GestureOnly/InkAndGesture/Select/EraseByPoint/EraseByStroke).");

                ink.EditingModeInverted = inverted;

                return true;

            case "Background":

                ink.Background = ParseBrush(value, lineInfo);

                return true;

            }

        }

        return false;

    }



    private static TextAlignment ParseTextAlignment(string value, IXmlLineInfo lineInfo)

    {

        if (!Enum.TryParse<TextAlignment>(value, ignoreCase: true, out var alignment))

            throw Fail(lineInfo, "'" + value + "' is not a TextAlignment (Left/Center/Right/Justify).");

        return alignment;

    }



    private static FlowDirection ParseFlowDirection(string value, IXmlLineInfo lineInfo)

    {

        if (!Enum.TryParse<FlowDirection>(value, ignoreCase: true, out var direction))

            throw Fail(lineInfo, "'" + value + "' is not a FlowDirection (LeftToRight/RightToLeft).");

        return direction;

    }



    private static DateTime? ParseCalendarDate(string value, IXmlLineInfo lineInfo, bool allowNull)
    {
        if (allowNull && string.Equals(value, "{x:Null}", StringComparison.OrdinalIgnoreCase)) return null;
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var date))
            return date.Date;
        throw Fail(lineInfo, "'" + value + "' is not a calendar date (yyyy-MM-dd).");
    }

    private static KeyboardNavigationMode ParseKeyboardNavigationMode(string value, IXmlLineInfo lineInfo)
    {
    try { return KeyboardNavigation.ParseMode(value); }
    catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
    }

    private static PanningMode ParsePanningMode(string value, IXmlLineInfo lineInfo)
    {
        if (!Enum.TryParse<PanningMode>(value, ignoreCase: true, out var panning))
            throw Fail(lineInfo, "'" + value + "' is not a PanningMode (None/HorizontalOnly/VerticalOnly/Both/HorizontalFirst/VerticalFirst).");
        return panning;
    }

    private static ScrollBarVisibility ParseScrollBarVisibility(string value, IXmlLineInfo lineInfo)

    {

        if (!Enum.TryParse<ScrollBarVisibility>(value, ignoreCase: true, out var visibility))

            throw Fail(lineInfo, "'" + value + "' is not a ScrollBarVisibility (Disabled/Auto/Hidden/Visible).");

        return visibility;

    }






    private static bool TrySetCustomProperty(UIElement element, string name, object? value, string? rawText, IXmlLineInfo lineInfo)
    {
        if (element.GetType().Assembly == typeof(FrameworkElement).Assembly) return false;
        var type = element.GetType();
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var prop = type.GetProperty(name, flags);
        if (prop is not null && prop.CanWrite && prop.SetMethod?.IsPublic == true)
        {
            object? converted;
            if (value is not null && prop.PropertyType.IsInstanceOfType(value)) converted = value;
            else if (rawText is not null)
            {
                try { converted = StyleValueConverter.Convert(rawText, prop.PropertyType); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException) { throw Fail(lineInfo, ex.Message); }
            }
            else return false;
            prop.SetValue(element, converted);
            if (converted is UIElement visual && visual.Parent is null) element.AttachVisualChild(visual);
            return true;
        }
        if (type.GetField(name + "Property", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) is DependencyProperty dp)
        {
            object? converted;
            if (value is not null && dp.PropertyType.IsInstanceOfType(value)) converted = value;
            else if (rawText is not null)
            {
                try { converted = StyleValueConverter.Convert(rawText, dp.PropertyType); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException) { throw Fail(lineInfo, ex.Message); }
            }
            else return false;
            element.SetValue(dp, converted);
            if (converted is UIElement visual && visual.Parent is null) element.AttachVisualChild(visual);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Converted-application assembly for <c>local:</c> control lookup
    /// (calculator MyTextBox). Set by <see cref="WpfAppBoot"/> around boot;
    /// null in engine-native scenes.
    /// </summary>
    public static System.Reflection.Assembly? CustomElementSource { get; set; }


private static void ApplyPlainAttribute(UIElement element, string local, string value, IXmlLineInfo lineInfo)
    {
        if (ApplyInkDocumentAttribute(element, local, value, lineInfo)) return;

        if (local.Contains('.'))
        {
            switch (local)
            {
            case "Grid.Row":
            {
                var gridRow = ParseInt(value, lineInfo);
                element.SetValue(Grid.RowProperty, gridRow);
                element.SetAttached(local, gridRow);
                break;
            }
            case "Grid.Column":
            {
                var gridColumn = ParseInt(value, lineInfo);
                element.SetValue(Grid.ColumnProperty, gridColumn);
                element.SetAttached(local, gridColumn);
                break;
            }
            case "Grid.RowSpan":
            {
                var gridRowSpan = ParseInt(value, lineInfo);
                element.SetValue(Grid.RowSpanProperty, gridRowSpan);
                element.SetAttached(local, gridRowSpan);
                break;
            }
            case "Grid.ColumnSpan":
            {
                var gridColumnSpan = ParseInt(value, lineInfo);
                element.SetValue(Grid.ColumnSpanProperty, gridColumnSpan);
                element.SetAttached(local, gridColumnSpan);
                break;
            }
            case "Canvas.Left":
            {
                var canvasLeft = ParseDouble(value, lineInfo);
                element.SetValue(Canvas.LeftProperty, canvasLeft);
                element.SetAttached(local, canvasLeft);
                break;
            }
            case "Canvas.Top":
            {
                var canvasTop = ParseDouble(value, lineInfo);
                element.SetValue(Canvas.TopProperty, canvasTop);
                element.SetAttached(local, canvasTop);
                break;
            }
            case "DockPanel.Dock":
            {
                if (DockPanel.TryParseDock(value, out var dock))
                {
                    element.SetAttached(local, dock);
                    break;
                }
                throw Fail(lineInfo, "'" + value + "' is not a Dock (Left/Top/Right/Bottom).");
            }
            case "WindowChrome.IsHitTestVisibleInChrome":
                element.SetAttached(local, ParseBool(value, lineInfo));
                break;
            case "PanelElement.FluidMoveBehavior":
                // Literal form of the retained Blend knob (the StaticResource
                // form lands through ApplyResolvedResource instead): retained
                // verbatim, never animated (see Slice111HandyPanels).
                element.SetAttached(local, value);
                element.SetValue(PanelElement.FluidMoveBehaviorProperty, value);
                break;
            case "AutomationProperties.Name":
            case "AutomationProperties.HelpText":
            case "AutomationProperties.AutomationId":
            case "AutomationProperties.HeadingLevel":
                ApplyAutomationProperty(element, local.Substring("AutomationProperties.".Length), value, lineInfo);
                break;
            case "ToolTipService.ToolTip":
                element.SetAttached(local, value);
                if (element is FrameworkElement toolTipOwner) ToolTipService.EnsureWired(toolTipOwner);
                break;
            case "ToolTipService.Placement":
                element.SetAttached(local, value);
                if (element is FrameworkElement placementOwner) ToolTipService.EnsureWired(placementOwner);
                break;
            case "ToolTipService.HorizontalOffset":
            case "ToolTipService.VerticalOffset":
                element.SetAttached(local, ParseDouble(value, lineInfo));
                if (element is FrameworkElement offsetOwner) ToolTipService.EnsureWired(offsetOwner);
                break;
            case "KeyboardNavigation.TabIndex":
                element.SetAttached(local, ParseInt(value, lineInfo));
                break;
            case "KeyboardNavigation.IsTabStop":
            {
                var tabStop = ParseBool(value, lineInfo);
                element.SetValue(KeyboardNavigation.IsTabStopProperty, tabStop);
                element.SetAttached(local, tabStop);
                break;
            }
            case "KeyboardNavigation.TabNavigation":
            {
                var tabNavigation = ParseKeyboardNavigationMode(value, lineInfo);
                element.SetValue(KeyboardNavigation.TabNavigationProperty, tabNavigation);
                element.SetAttached(local, tabNavigation);
                break;
            }
            case "KeyboardNavigation.DirectionalNavigation":
                element.SetAttached(local, ParseKeyboardNavigationMode(value, lineInfo));
                break;
            case "Grid.IsSharedSizeScope":
            {
                var sharedScope = ParseBool(value, lineInfo);
                element.SetValue(Grid.IsSharedSizeScopeProperty, sharedScope);
                element.SetAttached(local, sharedScope);
                break;
            }
            case "ScrollViewer.CanContentScroll":
            {
                var canScroll = ParseBool(value, lineInfo);
                element.SetValue(ScrollViewer.CanContentScrollProperty, canScroll);
                element.SetAttached(local, canScroll);
                break;
            }
            case "ScrollViewer.HorizontalScrollBarVisibility":
            {
                var horizontal = ParseScrollBarVisibility(value, lineInfo);
                element.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, horizontal);
                element.SetAttached(local, horizontal);
                break;
            }
            case "ScrollViewer.VerticalScrollBarVisibility":
            {
                var vertical = ParseScrollBarVisibility(value, lineInfo);
                element.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, vertical);
                element.SetAttached(local, vertical);
                break;
            }
            case "ScrollViewer.IsDeferredScrollingEnabled":
            {
                var deferred = ParseBool(value, lineInfo);
                element.SetValue(ScrollViewer.IsDeferredScrollingEnabledProperty, deferred);
                element.SetAttached(local, deferred);
                break;
            }
            case "ScrollViewer.PanningMode":
            {
                var panning = ParsePanningMode(value, lineInfo);
                element.SetValue(ScrollViewer.PanningModeProperty, panning);
                element.SetAttached(local, panning);
                break;
            }
            case "ScrollViewer.IsInertiaEnabled":
            {
                var inertia = ParseBool(value, lineInfo);
                element.SetValue(ScrollViewer.IsInertiaEnabledProperty, inertia);
                element.SetAttached(local, inertia);
                break;
            }
            case "Stylus.IsFlicksEnabled":
            {
                var flicks = ParseBool(value, lineInfo);
                element.SetValue(Stylus.IsFlicksEnabledProperty, flicks);
                element.SetAttached(local, flicks);
                break;
            }
            case "VirtualizingStackPanel.IsVirtualizing":
            {
                var virtualizing = ParseBool(value, lineInfo);
                element.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, virtualizing);
                element.SetAttached(local, virtualizing);
                break;
            }
            case "ToolTipService.InitialShowDelay":
                element.SetAttached(local, ParseInt(value, lineInfo));
                break;
            case "RenderOptions.EdgeMode":
            {
                if (!Enum.TryParse<EdgeMode>(value, ignoreCase: true, out var edge))
                    throw Fail(lineInfo, "'" + value + "' is not an EdgeMode (Unspecified/Aliased).");
                element.SetValue(RenderOptions.EdgeModeProperty, edge);
                element.SetAttached(local, edge);
                break;
            }
            case "FocusManager.IsFocusScope":
            {
                var scope = ParseBool(value, lineInfo);
                element.SetValue(FocusManager.IsFocusScopeProperty, scope);
                element.SetAttached(local, scope);
                break;
            }
            case "TextElement.Foreground":
                try
                {
                    element.SetAttached(local, new SolidColorBrush(StyleValueConverter.ColorFromRgba(ParseColorCore(value))));
                    break;
                }
                catch (ArgumentException ex)
                {
                    throw Fail(lineInfo, ex.Message);
                }
            default:
                if (TryApplyConvertedAttachedLiteral(element, local, value, lineInfo)) break;
                throw Fail(lineInfo, "Unknown attached property '" + local + "'.");
            }
            return;
        }
        bool flag;
        switch (local)
        {
        case "Row":
        case "Column":
        case "RowSpan":
        case "ColumnSpan":
            flag = true;
            break;
        default:
            flag = false;
            break;
        }
        if (flag)
        {
            element.SetAttached(local, ParseInt(value, lineInfo));
            return;
        }
        if (element is Shape shape && TryApplyShapeAttribute(shape, local, value, lineInfo))
            return;
        if (element is Popup popup)
        {
            switch (local)
            {
                case "Placement":
                    if (!Enum.TryParse<PlacementMode>(value, ignoreCase: true, out var placement))
                        throw Fail(lineInfo, "'" + value + "' is not a PlacementMode (Absolute/Relative/Bottom/Center/Right/Left/Top/Mouse/MousePoint/AbsolutePoint/RelativePoint/Custom).");
                    popup.Placement = placement;
                    return;
                case "IsOpen":
                    popup.IsOpen = ParseBool(value, lineInfo);
                    return;
                case "StaysOpen":
                    popup.StaysOpen = ParseBool(value, lineInfo);
                    return;
                case "AllowsTransparency":
                    popup.AllowsTransparency = ParseBool(value, lineInfo);
                    return;
                case "HorizontalOffset":
                    popup.HorizontalOffset = ParseDouble(value, lineInfo);
                    return;
                case "VerticalOffset":
                    popup.VerticalOffset = ParseDouble(value, lineInfo);
                    return;
            }
        }
        if (element is RepeatButton repeat)
        {
            switch (local)
            {
                case "Delay":
                    repeat.Delay = ParseInt(value, lineInfo);
                    return;
                case "Interval":
                    repeat.Interval = ParseInt(value, lineInfo);
                    return;
            }
        }
        if (element is GridSplitter splitter)
        {
            switch (local)
            {
                case "ResizeDirection":
                    if (!Enum.TryParse<GridResizeDirection>(value, ignoreCase: true, out var direction))
                        throw Fail(lineInfo, "'" + value + "' is not a GridResizeDirection (Auto/Columns/Rows).");
                    splitter.ResizeDirection = direction;
                    return;
                case "ResizeBehavior":
                    if (!Enum.TryParse<GridResizeBehavior>(value, ignoreCase: true, out var resizeBehavior))
                        throw Fail(lineInfo, "'" + value + "' is not a GridResizeBehavior (BasedOnAlignment/CurrentAndNext/PreviousAndCurrent/PreviousAndNext).");
                    splitter.ResizeBehavior = resizeBehavior;
                    return;
                case "ShowsPreview":
                    splitter.ShowsPreview = ParseBool(value, lineInfo);
                    return;
                case "DragIncrement":
                    splitter.DragIncrement = ParseDouble(value, lineInfo);
                    return;
                case "KeyboardIncrement":
                    splitter.KeyboardIncrement = ParseDouble(value, lineInfo);
                    return;
            }
        }
        if (element is TabControl tabs)
        {
            switch (local)
            {
                case "TabStripPlacement":
                    if (!Enum.TryParse<Dock>(value, ignoreCase: true, out var dock))
                        throw Fail(lineInfo, "'" + value + "' is not a Dock (Left/Top/Right/Bottom).");
                    tabs.TabStripPlacement = dock;
                    return;
            }
            if (ApplySelectorAttribute(tabs, local, value, lineInfo)) return;
            if (ApplyControlChrome(tabs, local, value, lineInfo)) return;
        }
        if (element is TabItem tabItem)
        {
            switch (local)
            {
                case "Header":
                    tabItem.Header = value;
                    return;
                case "Content":
                    tabItem.Content = value;
                    return;
                case "IsSelected":
                    tabItem.IsSelected = ParseBool(value, lineInfo);
                    return;
            }
            if (ApplyControlChrome(tabItem, local, value, lineInfo)) return;
        }
        if (element is ProgressBar bar)
        {
            switch (local)
            {
                case "Minimum":
                    bar.Minimum = ParseDouble(value, lineInfo);
                    return;
                case "Maximum":
                    bar.Maximum = ParseDouble(value, lineInfo);
                    return;
                case "Value":
                    bar.Value = ParseDouble(value, lineInfo);
                    return;
                case "Orientation":
                    if (!Enum.TryParse<Orientation>(value, ignoreCase: true, out var orientation))
                        throw Fail(lineInfo, "'" + value + "' is not an Orientation (Horizontal/Vertical).");
                    bar.Orientation = orientation;
                    return;
                case "IsIndeterminate":
                    bar.IsIndeterminate = ParseBool(value, lineInfo);
                    return;
            }
            if (ApplyControlChrome(bar, local, value, lineInfo)) return;
        }
        if (element is ToolTip tip)
        {
            switch (local)
            {
                case "Content":
                    tip.Content = value;
                    return;
                case "Placement":
                    if (!Enum.TryParse<PlacementMode>(value, ignoreCase: true, out var placement))
                        throw Fail(lineInfo, "'" + value + "' is not a PlacementMode (Absolute/Relative/Bottom/Center/Right/Left/Top/Mouse/MousePoint/AbsolutePoint/RelativePoint/Custom).");
                    tip.Placement = placement;
                    return;
                case "IsOpen":
                    tip.IsOpen = ParseBool(value, lineInfo);
                    return;
                case "StaysOpen":
                    tip.StaysOpen = ParseBool(value, lineInfo);
                    return;
                case "HorizontalOffset":
                    tip.HorizontalOffset = ParseDouble(value, lineInfo);
                    return;
                case "VerticalOffset":
                    tip.VerticalOffset = ParseDouble(value, lineInfo);
                    return;
            }
            if (ApplyControlChrome(tip, local, value, lineInfo)) return;
        }
        if (element is Calendar calendar)
        {
            switch (local)
            {
                case "SelectedDate":
                    calendar.SelectedDate = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "DisplayDate":
                    calendar.DisplayDate = ParseCalendarDate(value, lineInfo, allowNull: false) ?? calendar.DisplayDate;
                    return;
                case "DisplayDateStart":
                    calendar.DisplayDateStart = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "DisplayDateEnd":
                    calendar.DisplayDateEnd = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "DisplayMode":
                    if (!Enum.TryParse<CalendarMode>(value, ignoreCase: true, out var calendarMode))
                        throw Fail(lineInfo, "'" + value + "' is not a CalendarMode (Month/Year/Decade).");
                    calendar.DisplayMode = calendarMode;
                    return;
                case "SelectionMode":
                    if (!Enum.TryParse<CalendarSelectionMode>(value, ignoreCase: true, out var calendarSelection))
                        throw Fail(lineInfo, "'" + value + "' is not a CalendarSelectionMode (SingleDate/SingleRange/MultipleRange).");
                    calendar.SelectionMode = calendarSelection;
                    return;
                case "IsTodayHighlighted":
                    calendar.IsTodayHighlighted = ParseBool(value, lineInfo);
                    return;
                case "FirstDayOfWeek":
                    if (!Enum.TryParse<DayOfWeek>(value, ignoreCase: true, out var firstDay))
                        throw Fail(lineInfo, "'" + value + "' is not a DayOfWeek (Sunday/Monday/Tuesday/Wednesday/Thursday/Friday/Saturday).");
                    calendar.FirstDayOfWeek = firstDay;
                    return;
            }
            if (ApplyControlChrome(calendar, local, value, lineInfo)) return;
        }
        if (element is DatePicker datePicker)
        {
            switch (local)
            {
                case "SelectedDate":
                    datePicker.SelectedDate = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "DisplayDate":
                    datePicker.DisplayDate = ParseCalendarDate(value, lineInfo, allowNull: false) ?? datePicker.DisplayDate;
                    return;
                case "DisplayDateStart":
                    datePicker.DisplayDateStart = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "DisplayDateEnd":
                    datePicker.DisplayDateEnd = ParseCalendarDate(value, lineInfo, allowNull: true);
                    return;
                case "FirstDayOfWeek":
                    if (!Enum.TryParse<DayOfWeek>(value, ignoreCase: true, out var pickerFirstDay))
                        throw Fail(lineInfo, "'" + value + "' is not a DayOfWeek (Sunday/Monday/Tuesday/Wednesday/Thursday/Friday/Saturday).");
                    datePicker.FirstDayOfWeek = pickerFirstDay;
                    return;
                case "IsDropDownOpen":
                    datePicker.IsDropDownOpen = ParseBool(value, lineInfo);
                    return;
                case "IsTodayHighlighted":
                    datePicker.IsTodayHighlighted = ParseBool(value, lineInfo);
                    return;
                case "Text":
                    datePicker.Text = value;
                    return;
            }
            if (ApplyControlChrome(datePicker, local, value, lineInfo)) return;
        }
        if (element is RichTextBox richTextBox)
        {
            switch (local)
            {
                case "Document":
                    throw Fail(lineInfo, "'Document' needs a <RichTextBox.Document> property element (a FlowDocument).");
                case "IsReadOnly":
                    richTextBox.IsReadOnly = ParseBool(value, lineInfo);
                    return;
                case "AcceptsReturn":
                    richTextBox.AcceptsReturn = ParseBool(value, lineInfo);
                    return;
                case "AcceptsTab":
                    richTextBox.AcceptsTab = ParseBool(value, lineInfo);
                    return;
                case "VerticalScrollBarVisibility":
                    richTextBox.VerticalScrollBarVisibility = ParseScrollBarVisibility(value, lineInfo);
                    return;
                case "HorizontalScrollBarVisibility":
                    richTextBox.HorizontalScrollBarVisibility = ParseScrollBarVisibility(value, lineInfo);
                    return;
            }
            if (ApplyControlChrome(richTextBox, local, value, lineInfo)) return;
        }
        if (element is PasswordBox passwordBox)
        {
            switch (local)
            {
                case "Password":
                    passwordBox.Password = value;
                    return;
                case "PasswordChar":
                    if (value.Length != 1)
                        throw Fail(lineInfo, "'PasswordChar' takes a single character.");
                    passwordBox.PasswordChar = value[0];
                    return;
                case "MaxLength":
                    passwordBox.MaxLength = ParseInt(value, lineInfo);
                    return;
            }
            if (ApplyControlChrome(passwordBox, local, value, lineInfo)) return;
        }
        if (element is WebBrowser webBrowser)
        {
            if (local == "Source")
            {
                try { webBrowser.Source = new Uri(value, UriKind.RelativeOrAbsolute); }
                catch (UriFormatException ex) { throw Fail(lineInfo, ex.Message); }
                return;
            }
        }
        if (element is Panel panel && local == "IsItemsHost")
        {
            panel.IsItemsHost = ParseBool(value, lineInfo);
            return;
        }
        ItemsControl itemsControl;
        int num;
        Control control;
        if (!(element is Window window))
        {
            if (!(element is Grid grid))
            {
                if (!(element is StackPanel stackPanel))
                {
                    if (!(element is WrapPanel wrapPanel))
                    {
                        if (!(element is ComboBoxItem comboBoxItem))
                        {
                            if (!(element is ListViewItem listViewItem))
                            {
                                if (!(element is ComboBox comboBox))
                                {
                                    if (!(element is Selector selector))
                                    {
                                        if (!(element is HeaderedItemsControl headeredItemsControl))
                                        {
                                            itemsControl = element as ItemsControl;
                                            if (itemsControl != null)
                                            {
                                                num = 2;
                                                goto IL_0c8e;
                                            }
                                            if (!(element is Border border))
                                            {
                                                if (!(element is Label label))
                                                {
                                                    if (!(element is Button button))
                                                    {
                                                        if (!(element is Expander expander))
                                                        {
                                                            if (!(element is ToggleButton toggleButton))
                                                            {
                                                                if (!(element is Page page))
                                                                {
                                                                    if (!(element is Frame frame))
                                                                    {
                                                                        if (!(element is Hyperlink hyperlink))
                                                                        {
                                                                            if (!(element is Span span))
                                                                            {
                                                                                if (!(element is Path path))
                                                                                {
                                                                                    if (!(element is Rectangle rectangle))
                                                                                    {
                                                                                        if (!(element is Ellipse ellipse))
                                                                                        {
                                                                                            if (!(element is Viewbox viewbox))
                                                                                            {
                                                                                                if (!(element is TextBlock textBlock))
                                                                                                {
                                                                                                    if (!(element is TextBox textBox))
                                                                                                    {
                                                                                                        if (!(element is ScrollViewer scrollViewer))
                                                                                                        {
                                                                                                            if (!(element is ListBox listBox))
                                                                                                            {
                                                                                                                if (!(element is ListBoxItem listBoxItem))
                                                                                                                {
                                                                                                                    if (!(element is Menu menu))
                                                                                                                    {
                                                                                                                        if (!(element is MenuItem menuItem))
                                                                                                                        {
                                                                                                                            if (!(element is Slider slider))
                                                                                                                            {
                                                                                                                                control = element as Control;
                                                                                                                                if (control != null)
                                                                                                                                {
                                                                                                                                    goto IL_21d8;
                                                                                                                                }
                                                                                                                                if (!(element is DataGrid dataGrid))
                                                                                                                                {
                                                                                                                                    if (!(element is ContentPresenter contentPresenter))
                                                                                                                                    {
                                                                                                                                        if (!(element is Image image))
                                                                                                                                        {
                                                                                                                                            if (!(element is Canvas) && element is DockPanel dockPanel && local == "LastChildFill")
                                                                                                                                            {
                                                                                                                                                dockPanel.LastChildFill = ParseBool(value, lineInfo);
                                                                                                                                                return;
                                                                                                                                            }
                                                                                                                                        }
                                                                                                                                        else
                                                                                                                                        {
                                                                                                                                            if (local == "Source")
                                                                                                                                            {
                                                                                                                                                image.Source = new BitmapImage(new Uri(value, UriKind.RelativeOrAbsolute));
                                                                                                                                                return;
                                                                                                                                            }
                                                                                                                                            if (local == "Stretch")
                                                                                                                                            {
                                                                                                                                                if (!Enum.TryParse<Stretch>(value, ignoreCase: true, out var result))
                                                                                                                                                {
                                                                                                                                                    throw Fail(lineInfo, "'" + value + "' is not a Stretch (None/Fill/Uniform/UniformToFill).");
                                                                                                                                                }
                                                                                                                                                image.Stretch = result;
                                                                                                                                                return;
                                                                                                                                            }
                                                                                                                                        }
                                                                                                                                    }
                                                                                                                                    else
                                                                                                                                    {
                                                                                                                                        if (local == "RecognizesAccessKey")
                                                                                                                                        {
                                                                                                                                            contentPresenter.RecognizesAccessKey = ParseBool(value, lineInfo);
                                                                                                                                            return;
                                                                                                                                        }
                                                                                                                                        if (local == "Content")
                                                                                                                                        {
                                                                                                                                            contentPresenter.Content = value;
                                                                                                                                            return;
                                                                                                                                        }
                                                                                                                                        if (local == "ContentStringFormat")
                                                                                                                                        {
                                                                                                                                            contentPresenter.ContentStringFormat = value;
                                                                                                                                            return;
                                                                                                                                        }
                                                                                                                                    }
                                                                                                                                }
                                                                                                                                else if (local == "Foreground")
                                                                                                                                {
                                                                                                                                    dataGrid.Foreground = ParseBrush(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                }
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                switch (local)
                                                                                                                                {
                                                                                                                                case "Minimum":
                                                                                                                                    slider.Minimum = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "Maximum":
                                                                                                                                    slider.Maximum = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "Value":
                                                                                                                                    slider.Value = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "SmallChange":
                                                                                                                                    slider.SmallChange = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "LargeChange":
                                                                                                                                    slider.LargeChange = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "IsDirectionReversed":
                                                                                                                                    slider.IsDirectionReversed = ParseBool(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "IsSnapToTickEnabled":
                                                                                                                                    slider.IsSnapToTickEnabled = ParseBool(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "TickFrequency":
                                                                                                                                    slider.TickFrequency = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "TickPlacement":
                                                                                                                                {
                                                                                                                                    if (!Enum.TryParse<TickPlacement>(value, ignoreCase: true, out var result3))
                                                                                                                                    {
                                                                                                                                        throw Fail(lineInfo, "'" + value + "' is not a TickPlacement (None/TopLeft/BottomRight/Both).");
                                                                                                                                    }
                                                                                                                                    slider.TickPlacement = result3;
                                                                                                                                    return;
                                                                                                                                }
                                                                                                                                case "Orientation":
                                                                                                                                {
                                                                                                                                    if (!Enum.TryParse<Orientation>(value, ignoreCase: true, out var result2))
                                                                                                                                    {
                                                                                                                                        throw Fail(lineInfo, "'" + value + "' is not an Orientation (Horizontal/Vertical).");
                                                                                                                                    }
                                                                                                                                    slider.Orientation = result2;
                                                                                                                                    return;
                                                                                                                                }
                                                                                                                                case "Background":
                                                                                                                                    slider.Background = ParseBrush(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "Foreground":
                                                                                                                                    slider.Foreground = ParseBrush(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "BorderBrush":
                                                                                                                                    slider.BorderBrush = ParseBrush(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "BorderThickness":
                                                                                                                                    slider.BorderThickness = ParseThickness(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "Padding":
                                                                                                                                    slider.Padding = ParseThickness(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "FontFamily":
                                                                                                                                    slider.FontFamily = new FontFamily(value);
                                                                                                                                    return;
                                                                                                                                case "FontSize":
                                                                                                                                    slider.FontSize = ParseDouble(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "FontWeight":
                                                                                                                                    slider.FontWeight = ParseFontWeight(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                case "FontStyle":
                                                                                                                                    slider.FontStyle = ParseFontStyle(value, lineInfo);
                                                                                                                                    return;
                                                                                                                                }
                                                                                                                            }
                                                                                                                        }
                                                                                                                        else
                                                                                                                        {
                                                                                                                            switch (local)
                                                                                                                            {
                                                                                                                            case "Header":
                                                                                                                                menuItem.Header = value;
                                                                                                                                return;
                                                                                                                            case "IsEnabled":
                                                                                                                                menuItem.IsEnabled = ParseBool(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "Foreground":
                                                                                                                                menuItem.Foreground = ParseBrush(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "FontSize":
                                                                                                                                menuItem.FontCss = FontSizeToCss(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "IsCheckable":
                                                                                                                                menuItem.IsCheckable = ParseBool(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "IsChecked":
                                                                                                                                menuItem.IsChecked = ParseBool(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "Command":
                                                                                                                                menuItem.Command = ResolveStaticCommand(value, lineInfo);
                                                                                                                                return;
                                                                                                                            case "CommandParameter":
                                                                                                                                menuItem.CommandParameter = value;
                                                                                                                                return;
                                                                                                                            }
                                                                                                                        }
                                                                                                                    }
                                                                                                                    else if (local == "IsEnabled")
                                                                                                                    {
                                                                                                                        menu.IsEnabled = ParseBool(value, lineInfo);
                                                                                                                        return;
                                                                                                                    }
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    switch (local)
                                                                                                                    {
                                                                                                                    case "Content":
                                                                                                                        listBoxItem.Content = value;
                                                                                                                        return;
                                                                                                                    case "Foreground":
                                                                                                                        listBoxItem.Foreground = ParseBrush(value, lineInfo);
                                                                                                                        return;
                                                                                                                    case "Background":
                                                                                                                        listBoxItem.Background = ParseBrush(value, lineInfo);
                                                                                                                        return;
                                                                                                                    case "BorderBrush":
                                                                                                                        listBoxItem.BorderBrush = ParseBrush(value, lineInfo);
                                                                                                                        return;
                                                                                                                    case "BorderThickness":
                                                                                                                        listBoxItem.BorderThickness = ParseThickness(value, lineInfo);
                                                                                                                        return;
                                                                                                                    case "FontSize":
                                                                                                                        listBoxItem.FontCss = FontSizeToCss(value, lineInfo);
                                                                                                                        return;
                                                                                                                    }
                                                                                                                }
                                                                                                            }
                                                                                                            else if (local == "SelectedIndex")
                                                                                                            {
                                                                                                                listBox.SelectedIndex = ParseInt(value, lineInfo);
                                                                                                                return;
                                                                                                            }
                                                                                                            else if (local == "ItemsSource")
                                                                                                            {
                                                                                                                throw Fail(lineInfo, "ItemsSource= needs a {Binding} (a literal cannot name a source collection).");
                                                                                                            }
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            switch (local)
                                                                                                            {
                                                                                                            case "Padding":
                                                                                                                scrollViewer.Padding = ParseThickness(value, lineInfo);
                                                                                                                return;
                                                                                                            case "VerticalOffset":
                                                                                                                scrollViewer.VerticalOffset = ParseDouble(value, lineInfo);
                                                                                                                return;
                                                                                                            case "Background":
                                                                                                                scrollViewer.Background = ParseBrush(value, lineInfo);
                                                                                                                return;
                                                                                                            case "HorizontalScrollBarVisibility":
                                                                                                            {
                                                                                                                if (!Enum.TryParse<ScrollBarVisibility>(value, ignoreCase: true, out var result5))
                                                                                                                {
                                                                                                                    throw Fail(lineInfo, "'" + value + "' is not a ScrollBarVisibility (Disabled/Auto/Hidden/Visible).");
                                                                                                                }
                                                                                                                scrollViewer.HorizontalScrollBarVisibility = result5;
                                                                                                                return;
                                                                                                            }
                                                                                                            case "VerticalScrollBarVisibility":
                                                                                                            {
                                                                                                                if (!Enum.TryParse<ScrollBarVisibility>(value, ignoreCase: true, out var result4))
                                                                                                                {
                                                                                                                    throw Fail(lineInfo, "'" + value + "' is not a ScrollBarVisibility (Disabled/Auto/Hidden/Visible).");
                                                                                                                }
                                                                                                                scrollViewer.VerticalScrollBarVisibility = result4;
                                                                                                                return;
                                                                                                            }
                                                                                                            }
                                                                                                        }
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        switch (local)
                                                                                                        {
                                                                                                        case "Text":
                                                                                                            textBox.Text = value;
                                                                                                            return;
                                                                                                        case "Background":
                                                                                                            textBox.Background = ParseBrush(value, lineInfo);
                                                                                                            return;
                                                                                                        case "Foreground":
                                                                                                            textBox.Foreground = ParseBrush(value, lineInfo);
                                                                                                            return;
                                                                                                        case "FontSize":
                                                                                                            textBox.FontSize = ParseDouble(value, lineInfo);
                                                                                                            return;
                                                                                                        case "FontFamily":
                                                                                                            textBox.FontFamily = new FontFamily(value);
                                                                                                            return;
                                                                                                        case "FontWeight":
                                                                                                            textBox.FontWeight = ParseFontWeight(value, lineInfo);
                                                                                                            return;
                                                                                                        case "FontStyle":
                                                                                                            textBox.FontStyle = ParseFontStyle(value, lineInfo);
                                                                                                            return;
                                                                                                        case "TextWrapping":
                                                                                                        {
                                                                                                            if (!Enum.TryParse<TextWrapping>(value, ignoreCase: true, out var result6))
                                                                                                            {
                                                                                                                throw Fail(lineInfo, "'" + value + "' is not a TextWrapping (WrapWithOverflow/NoWrap/Wrap).");
                                                                                                            }
                                                                                                            textBox.TextWrapping = result6;
                                                                                                            return;
                                                                                                        }
                                                                                                        }
                                                                                                    }
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    switch (local)
                                                                                                    {
                                                                                                    case "Text":
                                                                                                        textBlock.Text = value;
                                                                                                        return;
                                                                                                    case "Background":
                                                                                                        textBlock.Background = ParseBrush(value, lineInfo);
                                                                                                        return;
                                                                                                    case "Foreground":
                                                                                                        textBlock.Foreground = ParseBrush(value, lineInfo);
                                                                                                        return;
                                                                                                    case "FontSize":
                                                                                                        textBlock.FontSize = ParseDouble(value, lineInfo);
                                                                                                        return;
                                                                                                    case "FontFamily":
                                                                                                        textBlock.FontFamily = new FontFamily(value);
                                                                                                        return;
                                                                                                    case "FontWeight":
                                                                                                        textBlock.FontWeight = ParseFontWeight(value, lineInfo);
                                                                                                        return;
                                                                                                    case "FontStyle":
                                                                                                        textBlock.FontStyle = ParseFontStyle(value, lineInfo);
                                                                                                        return;
                                                                                                    case "TextDecorations":
                                                                                                        try { textBlock.TextDecorations = TextDecorations.Parse(value); }
                                                                                                        catch (FormatException ex) { throw Fail(lineInfo, ex.Message); }
                                                                                                        return;
                                                                                                    case "Padding":
                                                                                                        textBlock.Padding = ParseThickness(value, lineInfo);
                                                                                                        return;
                                                                                                    case "TextAlignment":
                                                                                                    {
                                                                                                        if (!Enum.TryParse<TextAlignment>(value, ignoreCase: true, out var result9))
                                                                                                        {
                                                                                                            throw Fail(lineInfo, "'" + value + "' is not a TextAlignment (Left/Center/Right/Justify).");
                                                                                                        }
                                                                                                        textBlock.TextAlignment = result9;
                                                                                                        return;
                                                                                                    }
                                                                                                    case "TextTrimming":
                                                                                                    {
                                                                                                        if (!Enum.TryParse<TextTrimming>(value, ignoreCase: true, out var result8))
                                                                                                        {
                                                                                                            throw Fail(lineInfo, "'" + value + "' is not a TextTrimming (None/CharacterEllipsis/WordEllipsis).");
                                                                                                        }
                                                                                                        textBlock.TextTrimming = result8;
                                                                                                        return;
                                                                                                    }
                                                                                                    case "TextWrapping":
                                                                                                    {
                                                                                                        if (!Enum.TryParse<TextWrapping>(value, ignoreCase: true, out var result7))
                                                                                                        {
                                                                                                            throw Fail(lineInfo, "'" + value + "' is not a TextWrapping (WrapWithOverflow/NoWrap/Wrap).");
                                                                                                        }
                                                                                                        textBlock.TextWrapping = result7;
                                                                                                        return;
                                                                                                    }
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                            else if (local == "Stretch")
                                                                                            {
                                                                                                if (!Enum.TryParse<Stretch>(value, ignoreCase: true, out var result10))
                                                                                                {
                                                                                                    throw Fail(lineInfo, "'" + value + "' is not a Stretch (None/Fill/Uniform/UniformToFill).");
                                                                                                }
                                                                                                viewbox.Stretch = result10;
                                                                                                return;
                                                                                            }
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            switch (local)
                                                                                            {
                                                                                            case "Fill":
                                                                                                ellipse.Fill = ParseBrush(value, lineInfo);
                                                                                                return;
                                                                                            case "Stroke":
                                                                                                ellipse.Stroke = ParseBrush(value, lineInfo);
                                                                                                return;
                                                                                            case "StrokeThickness":
                                                                                                ellipse.StrokeThickness = ParseDouble(value, lineInfo);
                                                                                                return;
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        switch (local)
                                                                                        {
                                                                                        case "Fill":
                                                                                            rectangle.Fill = ParseBrush(value, lineInfo);
                                                                                            return;
                                                                                        case "Stroke":
                                                                                            rectangle.Stroke = ParseBrush(value, lineInfo);
                                                                                            return;
                                                                                        case "StrokeThickness":
                                                                                            rectangle.StrokeThickness = ParseDouble(value, lineInfo);
                                                                                            return;
                                                                                        case "RadiusX":
                                                                                            rectangle.RadiusX = ParseDouble(value, lineInfo);
                                                                                            return;
                                                                                        case "RadiusY":
                                                                                            rectangle.RadiusY = ParseDouble(value, lineInfo);
                                                                                            return;
                                                                                        }
                                                                                    }
                                                                                }
                                                                                else
                                                                                {
                                                                                    switch (local)
                                                                                    {
                                                                                    case "Data":
                                                                                        path.Data = ParseGeometry(value, lineInfo);
                                                                                        return;
                                                                                    case "Fill":
                                                                                        path.Fill = ParseBrush(value, lineInfo);
                                                                                        return;
                                                                                    case "Stroke":
                                                                                        path.Stroke = ParseBrush(value, lineInfo);
                                                                                        return;
                                                                                    case "StrokeThickness":
                                                                                        path.StrokeThickness = ParseDouble(value, lineInfo);
                                                                                        return;
                                                                                    }
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                switch (local)
                                                                                {
                                                                                case "Text":
                                                                                    span.Text = value;
                                                                                    return;
                                                                                case "Background":
                                                                                    span.Background = ParseBrush(value, lineInfo);
                                                                                    return;
                                                                                case "Foreground":
                                                                                    span.Foreground = ParseBrush(value, lineInfo);
                                                                                    return;
                                                                                case "FontFamily":
                                                                                    span.FontFamily = new FontFamily(value);
                                                                                    return;
                                                                                case "FontWeight":
                                                                                    span.FontWeight = ParseFontWeight(value, lineInfo);
                                                                                    return;
                                                                                }
                                                                            }
                                                                        }
                                                                        else
                                                                        {
                                                                            switch (local)
                                                                            {
                                                                            case "NavigateUri":
                                                                            {
                                                                                if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri result11))
                                                                                {
                                                                                    throw Fail(lineInfo, "'" + value + "' is not a NavigateUri (absolute or relative URI).");
                                                                                }
                                                                                hyperlink.NavigateUri = result11;
                                                                                return;
                                                                            }
                                                                            case "TargetName":
                                                                                hyperlink.TargetName = value;
                                                                                return;
                                                                            case "Text":
                                                                                hyperlink.Text = value;
                                                                                return;
                                                                            case "Background":
                                                                                hyperlink.Background = ParseBrush(value, lineInfo);
                                                                                return;
                                                                            case "Foreground":
                                                                                hyperlink.Foreground = ParseBrush(value, lineInfo);
                                                                                return;
                                                                            case "FontFamily":
                                                                                hyperlink.FontFamily = new FontFamily(value);
                                                                                return;
                                                                            case "FontWeight":
                                                                                hyperlink.FontWeight = ParseFontWeight(value, lineInfo);
                                                                                return;
                                                                            }
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        if (local == "NavigationUIVisibility")
                                                                        {
                                                                            frame.NavigationUIVisibility = ParseNavigationUIVisibility(value, lineInfo);
                                                                            return;
                                                                        }
                                                                        if (local == "Background")
                                                                        {
                                                                            frame.Background = ParseBrush(value, lineInfo);
                                                                            return;
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    if (local == "WindowTitle")
                                                                    {
                                                                        page.WindowTitle = value;
                                                                        return;
                                                                    }
                                                                    if (local == "Title")
                                                                    {
                                                                        page.Title = value;
                                                                        return;
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                switch (local)
                                                                {
                                                                case "Content":
                                                                    toggleButton.Content = value;
                                                                    return;
                                                                case "Foreground":
                                                                    toggleButton.Foreground = ParseBrush(value, lineInfo);
                                                                    return;
                                                                case "FontSize":
                                                                    toggleButton.FontSize = ParseDouble(value, lineInfo);
                                                                    return;
                                                                case "FontFamily":
                                                                    toggleButton.FontFamily = new FontFamily(value);
                                                                    return;
                                                                case "FontWeight":
                                                                    toggleButton.FontWeight = ParseFontWeight(value, lineInfo);
                                                                    return;
                                                                case "FontStyle":
                                                                    toggleButton.FontStyle = ParseFontStyle(value, lineInfo);
                                                                    return;
                                                                case "IsChecked":
                                                                    toggleButton.IsChecked = ParseNullableBool(value, lineInfo);
                                                                    return;
                                                                case "GroupName":
                                                                    if (toggleButton is RadioButton radioButton)
                                                                    {
                                                                        radioButton.GroupName = value;
                                                                        return;
                                                                    }
                                                                    break;
                                                                }
                                                                if (local == "IsThreeState" && toggleButton is CheckBox checkBox)
                                                                {
                                                                    checkBox.IsThreeState = ParseBool(value, lineInfo);
                                                                    return;
                                                                }
                                                                switch (local)
                                                                {
                                                                case "BorderBrush":
                                                                    toggleButton.BorderBrush = ParseBrush(value, lineInfo);
                                                                    return;
                                                                case "BorderThickness":
                                                                    toggleButton.BorderThickness = ParseThickness(value, lineInfo);
                                                                    return;
                                                                case "Padding":
                                                                    toggleButton.Padding = ParseThickness(value, lineInfo);
                                                                    return;
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            switch (local)
                                                            {
                                                            case "Header":
                                                                expander.Header = value;
                                                                return;
                                                            case "Content":
                                                                expander.Content = value;
                                                                return;
                                                            case "IsExpanded":
                                                                expander.IsExpanded = ParseBool(value, lineInfo);
                                                                return;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        switch (local)
                                                        {
                                                        case "Content":
                                                            button.Content = value;
                                                            return;
                                                        case "Background":
                                                            button.Background = ParseBrush(value, lineInfo);
                                                            return;
                                                        case "Foreground":
                                                            button.Foreground = ParseBrush(value, lineInfo);
                                                            return;
                                                        case "FontSize":
                                                            button.FontSize = ParseDouble(value, lineInfo);
                                                            return;
                                                        case "FontFamily":
                                                            button.FontFamily = new FontFamily(value);
                                                            return;
                                                        case "FontWeight":
                                                            button.FontWeight = ParseFontWeight(value, lineInfo);
                                                            return;
                                                        case "FontStyle":
                                                            button.FontStyle = ParseFontStyle(value, lineInfo);
                                                            return;
                                                        case "BorderBrush":
                                                            button.BorderBrush = ParseBrush(value, lineInfo);
                                                            return;
                                                        case "BorderThickness":
                                                            button.BorderThickness = ParseThickness(value, lineInfo);
                                                            return;
                                                        case "Padding":
                                                            button.Padding = ParseThickness(value, lineInfo);
                                                            return;
                                                        case "CommandParameter":
                                                            button.CommandParameter = value;
                                                            return;
                                                        case "Command":
                                                            if (TryResolveLiteralCommand(value, lineInfo, out var literalCommand))
                                                            {
                                                                button.Command = literalCommand;
                                                                return;
                                                            }
                                                            throw Fail(lineInfo, "Command= needs a {Binding} or code assignment (a literal name cannot resolve without a namescope).");
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    switch (local)
                                                    {
                                                    case "Content":
                                                        label.Content = value;
                                                        return;
                                                    case "ContentStringFormat":
                                                        label.ContentStringFormat = value;
                                                        return;
                                                    case "Foreground":
                                                        label.Foreground = ParseBrush(value, lineInfo);
                                                        return;
                                                    case "FontSize":
                                                        label.FontSize = ParseDouble(value, lineInfo);
                                                        return;
                                                    case "FontFamily":
                                                        label.FontFamily = new FontFamily(value);
                                                        return;
                                                    case "FontWeight":
                                                        label.FontWeight = ParseFontWeight(value, lineInfo);
                                                        return;
                                                    case "FontStyle":
                                                        label.FontStyle = ParseFontStyle(value, lineInfo);
                                                        return;
                                                    case "Padding":
                                                        label.Padding = ParseThickness(value, lineInfo);
                                                        return;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                switch (local)
                                                {
                                                case "Background":
                                                    border.Background = ParseBrush(value, lineInfo);
                                                    return;
                                                case "Thickness":
                                                    border.Thickness = ParseDouble(value, lineInfo);
                                                    return;
                                                case "BorderThickness":
                                                    border.BorderThickness = ParseThickness(value, lineInfo);
                                                    return;
                                                case "CornerRadius":
                                                    try
                                                    {
                                                        border.CornerRadius = CornerRadius.Parse(value);
                                                        return;
                                                    }
                                                    catch (FormatException ex2)
                                                    {
                                                        throw Fail(lineInfo, ex2.Message);
                                                    }
                                                case "Padding":
                                                    border.Padding = ParseThickness(value, lineInfo);
                                                    return;
                                                case "BorderBrush":
                                                    border.BorderBrush = ParseBrush(value, lineInfo);
                                                    return;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (headeredItemsControl is TreeViewItem)
                                            {
                                                itemsControl = (ItemsControl)element;
                                                num = 1;
                                                goto IL_0c8e;
                                            }
                                            if (local == "Header")
                                            {
                                                headeredItemsControl.Header = value;
                                                return;
                                            }
                                        }
                                    }
                                    else if (ApplySelectorAttribute(selector, local, value, lineInfo))
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    switch (local)
                                    {
                                    case "IsDropDownOpen":
                                        comboBox.IsDropDownOpen = ParseBool(value, lineInfo);
                                        return;
                                    case "IsEditable":
                                        comboBox.IsEditable = ParseBool(value, lineInfo);
                                        return;
                                    case "Text":
                                        comboBox.Text = value;
                                        return;
                                    case "MaxDropDownHeight":
                                        comboBox.MaxDropDownHeight = ParseDouble(value, lineInfo);
                                        return;
                                    }
                                    if (ApplySelectorAttribute(comboBox, local, value, lineInfo))
                                    {
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                switch (local)
                                {
                                case "Content":
                                    listViewItem.Content = value;
                                    return;
                                case "IsSelected":
                                    listViewItem.IsSelected = ParseBool(value, lineInfo);
                                    return;
                                case "Header":
                                    listViewItem.Header = value;
                                    return;
                                }
                            }
                        }
                        else
                        {
                            switch (local)
                            {
                            case "Content":
                                comboBoxItem.Content = value;
                                return;
                            case "IsSelected":
                                comboBoxItem.IsSelected = ParseBool(value, lineInfo);
                                return;
                            case "Header":
                                comboBoxItem.Header = value;
                                return;
                            }
                        }
                    }
                    else if (local == "Orientation")
                    {
                        if (!Enum.TryParse<Orientation>(value, ignoreCase: true, out var result12))
                        {
                            throw Fail(lineInfo, "'" + value + "' is not an Orientation (Horizontal/Vertical).");
                        }
                        wrapPanel.Orientation = result12;
                        return;
                    }
                }
                else if (local == "Orientation")
                {
                    if (!Enum.TryParse<Orientation>(value, ignoreCase: true, out var result13))
                    {
                        throw Fail(lineInfo, "'" + value + "' is not an Orientation (Horizontal/Vertical).");
                    }
                    stackPanel.Orientation = result13;
                    return;
                }
            }
            else
            {
                if (local == "RowDefinitions" || local == "ColumnDefinitions")
                {
                    ParseGridShorthandDefinitions(grid, local == "RowDefinitions", value, lineInfo);
                    return;
                }
                if (local == "Rows")
                {
                    grid.Rows = ParseInt(value, lineInfo);
                    return;
                }
                if (local == "Cols" || local == "Columns")
                {
                    grid.Cols = ParseInt(value, lineInfo);
                    return;
                }
                if (local == "Background")
                {
                    grid.Background = ParseBrush(value, lineInfo);
                    return;
                }
                if (local == "ShowGridLines")
                {
                    grid.ShowGridLines = ParseBool(value, lineInfo);
                    return;
                }
            }
        }
        else
        {
            switch (local)
            {
            case "Title":
                window.Title = value;
                return;
            case "Icon":
                window.IconSource = value;
                window.Icon = new BitmapImage(new Uri(value, UriKind.RelativeOrAbsolute));
                return;
            case "Left":
                window.Left = ParseDouble(value, lineInfo);
                return;
            case "Top":
                window.Top = ParseDouble(value, lineInfo);
                return;
            case "AllowsTransparency":
                window.AllowsTransparency = ParseBool(value, lineInfo);
                return;
            case "ShowActivated":
                window.ShowActivated = ParseBool(value, lineInfo);
                return;
            case "ResizeMode":
            {
                if (!Enum.TryParse<ResizeMode>(value, ignoreCase: true, out var result17))
                {
                    throw Fail(lineInfo, "'" + value + "' is not a ResizeMode (NoResize/CanMinimize/CanResize/CanResizeWithGrip).");
                }
                window.ResizeMode = result17;
                return;
            }
            case "WindowStyle":
            {
                if (!Enum.TryParse<WindowStyle>(value, ignoreCase: true, out var result15))
                {
                    throw Fail(lineInfo, "'" + value + "' is not a WindowStyle (None/SingleBorderWindow/ThreeDBorderWindow/ToolWindow).");
                }
                window.WindowStyle = result15;
                return;
            }
            case "WindowState":
            {
                if (!Enum.TryParse<WindowState>(value, ignoreCase: true, out var result16))
                {
                    throw Fail(lineInfo, "'" + value + "' is not a WindowState (Normal/Minimized/Maximized).");
                }
                window.WindowState = result16;
                return;
            }
            case "WindowStartupLocation":
            {
                if (!Enum.TryParse<WindowStartupLocation>(value, ignoreCase: true, out var result14))
                {
                    throw Fail(lineInfo, "'" + value + "' is not a WindowStartupLocation (Manual/CenterScreen/CenterOwner).");
                }
                window.WindowStartupLocation = result14;
                return;
            }
            case "ShowInTaskbar":
                window.ShowInTaskbar = ParseBool(value, lineInfo);
                return;
            case "Topmost":
                window.Topmost = ParseBool(value, lineInfo);
                return;
            case "SizeToContent":
            {
                if (!Enum.TryParse<SizeToContent>(value, ignoreCase: true, out var sizeToContent))
                {
                    throw Fail(lineInfo, "'" + value + "' is not a SizeToContent (Manual/Width/Height/WidthAndHeight).");
                }
                window.SizeToContent = sizeToContent;
                return;
            }
            }
        }
        goto IL_24fd;
        IL_0caf:
        switch (local)
        {
        case "DisplayMemberPath":
            itemsControl.DisplayMemberPath = value;
            return;
        case "AlternationCount":
            itemsControl.AlternationCount = ParseInt(value, lineInfo);
            return;
        case "ItemsSource":
            throw Fail(lineInfo, "ItemsSource= needs a {Binding} (a literal cannot name a source collection).");
        }
        goto IL_24fd;
        IL_21d8:
        switch (local)
        {
        case "Background":
            control.Background = ParseBrush(value, lineInfo);
            return;
        case "Foreground":
            control.Foreground = ParseBrush(value, lineInfo);
            return;
        case "BorderBrush":
            control.BorderBrush = ParseBrush(value, lineInfo);
            return;
        case "BorderThickness":
            control.BorderThickness = ParseThickness(value, lineInfo);
            return;
        case "Padding":
            control.Padding = ParseThickness(value, lineInfo);
            return;
        case "FontFamily":
            control.FontFamily = new FontFamily(value);
            return;
        case "FontSize":
            control.FontSize = ParseDouble(value, lineInfo);
            return;
        case "FontWeight":
            control.FontWeight = ParseFontWeight(value, lineInfo);
            return;
        case "FontStyle":
            control.FontStyle = ParseFontStyle(value, lineInfo);
            return;
        case "HorizontalContentAlignment":
            control.HorizontalContentAlignment = ParseHAlign(value, lineInfo);
            return;
        case "VerticalContentAlignment":
            control.VerticalContentAlignment = ParseVAlign(value, lineInfo);
            return;
        }
        goto IL_24fd;
        IL_0c8e:
        if (!(itemsControl is TreeViewItem))
        {
            goto IL_0caf;
        }
        if (num == 1)
        {
            if (element is TreeViewItem treeViewItem)
            {
                switch (local)
                {
                case "Header":
                    treeViewItem.Header = value;
                    return;
                case "IsExpanded":
                    treeViewItem.IsExpanded = ParseBool(value, lineInfo);
                    return;
                case "Foreground":
                    treeViewItem.Foreground = ParseBrush(value, lineInfo);
                    return;
                case "FontSize":
                    treeViewItem.FontCss = FontSizeToCss(value, lineInfo);
                    return;
                }
                goto IL_24fd;
            }
        }
        else if (num != 2)
        {
            goto IL_0caf;
        }
        control = (Control)element;
        goto IL_21d8;
        IL_24fd:
        FrameworkElement frameworkElement = element as FrameworkElement;
        bool flag2 = frameworkElement != null;
        bool flag3 = flag2;
        if (flag3)
        {
            flag = ((local == "Width" || local == "Height") ? true : false);
            flag3 = flag;
        }
        if (flag3)
        {
            double num2 = ParseLength(value, lineInfo);
            if (local == "Width")
            {
                frameworkElement.Width = num2;
            }
            else
            {
                frameworkElement.Height = num2;
            }
            return;
        }
        if (element is FrameworkElement frameworkElement2)
        {
            switch (local)
            {
            case "HorizontalAlignment":
                frameworkElement2.HorizontalAlignment = ParseHAlign(value, lineInfo);
                return;
            case "VerticalAlignment":
                frameworkElement2.VerticalAlignment = ParseVAlign(value, lineInfo);
                return;
            case "Margin":
                frameworkElement2.Margin = ParseThickness(value, lineInfo);
                return;
            case "ToolTip":
                frameworkElement2.ToolTip = value;
                return;
            case "Visibility":
                frameworkElement2.Visibility = ParseVisibility(value, lineInfo);
                return;
            case "IsHitTestVisible":
                frameworkElement2.IsHitTestVisible = ParseBool(value, lineInfo);
                return;
            case "MaxWidth":
                frameworkElement2.MaxWidth = ParseLength(value, lineInfo);
                return;
            case "MaxHeight":
                frameworkElement2.MaxHeight = ParseLength(value, lineInfo);
                return;
            case "MinWidth":
                frameworkElement2.MinWidth = ParseLength(value, lineInfo);
                return;
            case "MinHeight":
                frameworkElement2.MinHeight = ParseLength(value, lineInfo);
                return;
            case "ClipToBounds":
                frameworkElement2.ClipToBounds = ParseBool(value, lineInfo);
                return;
            case "Cursor":
                frameworkElement2.Cursor = StyleValueConverter.ParseCursor(value);
                return;
            case "Focusable":
                frameworkElement2.Focusable = ParseBool(value, lineInfo);
                return;
            case "AllowDrop":
                frameworkElement2.AllowDrop = ParseBool(value, lineInfo);
                return;
            case "SnapsToDevicePixels":
                frameworkElement2.SnapsToDevicePixels = ParseBool(value, lineInfo);
                return;
            case "OverridesDefaultStyle":
                frameworkElement2.OverridesDefaultStyle = ParseBool(value, lineInfo);
                return;
            case "RenderTransformOrigin":
                try
                {
                    frameworkElement2.RenderTransformOrigin = Point.Parse(value);
                    return;
                }
                catch (FormatException ex3)
                {
                    throw Fail(lineInfo, ex3.Message);
                }
            case "RenderTransform":
                throw Fail(lineInfo, "RenderTransform= needs a property element (<FrameworkElement.RenderTransform>).");
            case "Opacity":
            {
                double num3 = ParseDouble(value, lineInfo);
                if (num3 != 1.0)
                {
                    throw Fail(lineInfo, "Opacity '" + value + "' stays a precise boundary (v1 only loads the default 1.0; no translucency compositing).");
                }
                return;
            }
            }
        }
        System.Reflection.PropertyInfo? property = element.GetType().GetProperty(local, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy);
        if (property is not null && property.CanWrite)
        {
            System.Reflection.MethodInfo? setMethod = property.SetMethod;
            if (setMethod is not null && setMethod.IsPublic && property.PropertyType == typeof(Brush))
            {
                property.SetValue(element, ParseBrush(value, lineInfo));
                element.InvalidateVisual();
                return;
            }
        }
        if (element is Control control2)
        {
            if (local == "HorizontalContentAlignment")
            {
                control2.HorizontalContentAlignment = ParseHAlign(value, lineInfo);
                return;
            }
            if (local == "VerticalContentAlignment")
            {
                control2.VerticalContentAlignment = ParseVAlign(value, lineInfo);
                return;
            }
        }
        if (element is Page pageElement && local is "Background" or "Foreground")
        {
            var pageBrush = ParseBrush(value, lineInfo);
            if (local == "Background") pageElement.Background = pageBrush;
            else pageElement.Foreground = pageBrush;
            return;
        }
        if (TrySetCustomProperty(element, local, value, value, lineInfo)) return;
        if (TrySetPlainClrProperty(element, local, value, lineInfo)) return;
        throw Fail(lineInfo, $"'{local}' is not valid on <{element.GetType().Name}>.");
    }

    private static UIElement CreateElement(string name, string namespaceUri, IXmlLineInfo lineInfo)
    {
        // Slice-49: staged converted OxyPlot views win over the engine shims.
        // oxyWpf:PlotView and local:XamlPlotView resolve to the converted
        // assemblies; oxySkia:PlotView has no staged image
        // (OxyPlot.SkiaSharp.Wpf is conversion-blocked) and engine-native
        // <PlotView>/<XamlPlotView> in the presentation namespace keeps the
        // honest-empty shim (never fabricate plot pixels).
        if (name is "PlotView" or "XamlPlotView" && !IsPresentationNamespace(namespaceUri))
        {
            var converted = TryCreateConvertedPlotHost(name, namespaceUri);
            if (converted is not null) return converted;
        }
        var element = name switch
        {
        "Window" => new Window(),
        "Control" => new Control(),
        "UserControl" => new UserControl(),
        "Image" => new Image(),
        "Grid" => new Grid(),
        "StackPanel" => new StackPanel(),
        "Canvas" => new Canvas(),
        "DockPanel" => new DockPanel(),
        "WrapPanel" => new WrapPanel(),
"UniformGrid" => new UniformGrid(),
        "VirtualizingStackPanel" => new VirtualizingStackPanel(),
        "SimplePanel" => new SimplePanel(),
        "TransitioningContentControl" => new TransitioningContentControl(),
        "Border" => new Border(),
        "Decorator" => new Decorator(),
        "AdornerDecorator" => new AdornerDecorator(),
        "AdornedElementPlaceholder" => new AdornedElementPlaceholder(),
        "HeaderedContentControl" => new HeaderedContentControl(),
        "ContentControl" => new ContentControl(),
        "Expander" => new Expander(),
        "GroupBox" => new GroupBox(),
        "Button" => new Button(),
        "ToggleButton" => new ToggleButton(),
        "CheckBox" => new CheckBox(),
        "RadioButton" => new RadioButton(),
        "Page" => new Page(),
        "Frame" => new Frame(),
        "Label" => new Label(),
        "TextBlock" => new TextBlock(),
        "TextBox" => new TextBox(),
        "PasswordBox" => new PasswordBox(),
        "WebBrowser" => new WebBrowser(),
        "ScrollViewer" => new ScrollViewer(),
        "Separator" => new Separator(),
        "Slider" => new Slider(),
        "ScrollBar" => new ScrollBar(),
        "Thumb" => new Thumb(),
        "RepeatButton" => new RepeatButton(),
        "GridSplitter" => new GridSplitter(),
        "Popup" => new Popup(),
        "TabControl" => new TabControl(),
        "TabItem" => new TabItem(),
        "TabPanel" => new TabPanel(),
        "ProgressBar" => new ProgressBar(),
        "ToolTip" => new ToolTip(),
        "Calendar" => new Calendar(),
        "CalendarButton" => new CalendarButton(),
        "CalendarDayButton" => new CalendarDayButton(),
        "CalendarItem" => new CalendarItem(),
        "DatePicker" => new DatePicker(),
        "DatePickerTextBox" => new DatePickerTextBox(),
        "RichTextBox" => new RichTextBox(),
        "ListBox" => new ListBox(),
        "ListBoxItem" => new ListBoxItem(),
        "GroupItem" => new GroupItem(),
        "ItemsPresenter" => new ItemsPresenter(),
        "ItemsControl" => new ItemsControl(),
        "HeaderedItemsControl" => new HeaderedItemsControl(),
        "ComboBox" => new ComboBox(),
        "ComboBoxItem" => new ComboBoxItem(),
        "ListView" => new ListView(),
        "ListViewItem" => new ListViewItem(),
        "TreeView" => new TreeView(),
        "TreeViewItem" => new TreeViewItem(),
        "Menu" => new Menu(),
        "MenuItem" => new MenuItem(),
        "ContextMenu" => new ContextMenu(),
        "ToolBarTray" => new ToolBarTray(),
        "ToolBar" => new ToolBar(),
        "ToolBarPanel" => new ToolBarPanel(),
        "ToolBarOverflowPanel" => new ToolBarOverflowPanel(),
        "DataGrid" => new DataGrid(),
        "DataGridColumnHeader" => new DataGridColumnHeader(),
        "DataGridRowHeader" => new DataGridRowHeader(),
        "DataGridColumnHeadersPresenter" => new DataGridColumnHeadersPresenter(),
        "DataGridRowsPresenter" => new DataGridRowsPresenter(),
        "DataGridRow" => new DataGridRow(),
        "DataGridCell" => new DataGridCell(),
        "DataGridCellsPresenter" => new DataGridCellsPresenter(),
        "DataGridDetailsPresenter" => new DataGridDetailsPresenter(),
        "GridViewRowPresenter" => new GridViewRowPresenter(),
        "GridViewHeaderRowPresenter" => new GridViewHeaderRowPresenter(),
        "GridViewColumnHeader" => new GridViewColumnHeader(),
        "SelectiveScrollingGrid" => new SelectiveScrollingGrid(),
        "Viewport3D" => new Viewport3D(),
        "Path" => new Path(),
        "Line" => new Line(),
        "Polyline" => new Polyline(),
        "Polygon" => new Polygon(),
        "Rectangle" => new Rectangle(),
        "Ellipse" => new Ellipse(),
        "Viewbox" => new Viewbox(),
        "ContentPresenter" => new ContentPresenter(),
        "ScrollContentPresenter" => new ScrollContentPresenter(),
        "Track" => new Track(),
        "TickBar" => new TickBar(),
        "StatusBarItem" => new StatusBarItem(),
        "Span" => new Span(),
        "Run" => new Run(),
        "LineBreak" => new LineBreak(),
        "Hyperlink" => new Hyperlink(),
        "Bold" => new Bold(),
        "Italic" => new Italic(),
        "Underline" => new Underline(),
        "InkCanvas" => new InkCanvas(),

        "FlowDocument" => new FlowDocument(),

        "FlowDocumentScrollViewer" => new FlowDocumentScrollViewer(),

        "Paragraph" => new Paragraph(),

        "Inline" => new Inline(),

        // Slice-49: honest OxyPlot host placeholders (see PlotView.cs) for
        // engine-native XAML and the unstaged oxySkia view. Staged converted
        // oxyWpf:/local: elements resolve to the converted assemblies
        // through TryCreateConvertedPlotHost before this switch runs.
        "PlotView" => new PlotView(),

        "XamlPlotView" => new XamlPlotView(),

        _ => CreateCustomElement(name, namespaceUri, lineInfo),
        };

        return element;
    }

    /// <summary>
    /// Presentation-namespace check: engine-native XAML (the WPF
    /// presentation namespace, or no namespace at all) keeps engine
    /// types. Converted prefixes (oxyWpf:, oxySkia:, local:) carry
    /// clr-namespace xmlns URIs and resolve to converted assemblies.
    /// </summary>
    private static bool IsPresentationNamespace(string namespaceUri) =>
        string.IsNullOrEmpty(namespaceUri) ||
        namespaceUri.StartsWith("http://schemas.microsoft.com/", StringComparison.Ordinal);

    /// <summary>
    /// Slice-49 converted plot-host precedence: finds a staged converted
    /// PlotView/XamlPlotView (OxyPlot.Wpf, or the entry-local XamlPlotView)
    /// without ever returning the engine shim. A clr-namespace xmlns
    /// ("clr-namespace:OxyPlot.Wpf;assembly=OxyPlot.Wpf") maps to that
    /// exact full name; anything else falls back to a single-match
    /// short-name scan like <see cref="CreateCustomElement"/>. Zero or
    /// ambiguous matches, unloadable types, or a missing converted image
    /// (oxySkia today) return null so the caller keeps the shim.
    /// Instantiation itself stays loud: a resolved converted type whose
    /// constructor throws surfaces the real contract gap instead of
    /// silently rebinding to the shim.
    /// </summary>
    private static UIElement? TryCreateConvertedPlotHost(string name, string namespaceUri)
    {
        var targetFullName = ClrNamespaceTarget(namespaceUri, name);
        var engine = typeof(UIElement).Assembly;
        var assemblies = new List<System.Reflection.Assembly>();
        if (CustomElementSource is not null) assemblies.Add(CustomElementSource);
        assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
        if (targetFullName is not null)
        {
            foreach (var asm in assemblies.Distinct())
            {
                if (asm == engine) continue;
                Type? type;
                try { type = asm.GetType(targetFullName, throwOnError: false, ignoreCase: false); }
                catch { continue; }
                if (type is null || !typeof(UIElement).IsAssignableFrom(type) ||
                    type.IsAbstract || !HasDefaultCtor(type)) continue;
                return (UIElement)Activator.CreateInstance(type, nonPublic: true)!;
            }
            return null;
        }
        var matches = new List<Type>();
        foreach (var asm in assemblies.Distinct())
        {
            if (asm == engine) continue;
            List<Type> candidates;
            try
            {
                candidates = WpfAppBoot.SafeGetTypes(asm)
                    .Where(t => t.Name == name && typeof(UIElement).IsAssignableFrom(t) &&
                                !t.IsAbstract && HasDefaultCtor(t))
                    .ToList();
            }
            catch { continue; }
            matches.AddRange(candidates);
        }
        if (matches.Count != 1) return null;
        return (UIElement)Activator.CreateInstance(matches[0], nonPublic: true)!;
    }

    /// <summary>
    /// Maps a clr-namespace xmlns ("clr-namespace:OxyPlot.Wpf" with an
    /// optional ";assembly=" suffix) plus a local element name to the
    /// converted full type name. Returns null for non-clr namespaces.
    /// </summary>
    private static string? ClrNamespaceTarget(string namespaceUri, string name)
    {
        const string prefix = "clr-namespace:";
        if (!namespaceUri.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = namespaceUri.Substring(prefix.Length);
        var cut = rest.IndexOf(';');
        if (cut >= 0) rest = rest.Substring(0, cut);
        rest = rest.Trim();
        if (rest.Length == 0) return null;
        return rest + "." + name;
    }

    private static bool HasDefaultCtor(Type type) =>
        type.GetConstructor(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance,
            null, Type.EmptyTypes, null) is not null;

    /// <summary>
    /// Instantiates a converted-app control by short type name (calculator
    /// MyTextBox). Ambiguous or missing types are precise errors; the lookup
    /// is by name only (converted apps keep one control per short name).
    /// </summary>
    private static UIElement CreateCustomElement(string name, string namespaceUri, IXmlLineInfo lineInfo)
    {
        var source = CustomElementSource;
        if (source is not null)
        {
            // Namespace-qualified names bind exact first (converted style
            // targets and template fixtures use the full CLR name); the scan
            // below stays the short-name path.
            if (name.Contains('.'))
            {
                Type? direct = null;
                try { direct = source.GetType(name); }
                catch { direct = null; }
                if (direct is not null && typeof(UIElement).IsAssignableFrom(direct) && !direct.IsAbstract &&
                    direct.GetConstructor(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance,
                        null, Type.EmptyTypes, null) is not null)
                    return (UIElement)Activator.CreateInstance(direct, nonPublic: true)!;
            }
            if (XmlNamespacesFor(namespaceUri) is not null || namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                // Mapped prefixes bind namespace-aware first (wpf:SmartHint is
                // the vendor control, not an entry-local short-name
                // coincidence); the entry and global short-name scans stay
                // as fallbacks below, like WPF prefix semantics.
                var mapped = TryCreateConvertedHandyElement(name, namespaceUri, lineInfo);
                if (mapped is not null) return mapped;
                var namespaced = TryCreateClrNamespaceElement(name, namespaceUri, lineInfo);
                if (namespaced is not null) return namespaced;
            }
            // Non-public controls (internal MyTextBox) instantiate too.
            var matches = WpfAppBoot.SafeGetTypes(source)
                .Where(t => t.Name == name && typeof(UIElement).IsAssignableFrom(t) && !t.IsAbstract &&
                            t.GetConstructor(
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance,
                                null, Type.EmptyTypes, null) is not null)
                .ToList();
            if (matches.Count == 1)
                return (UIElement)Activator.CreateInstance(matches[0], nonPublic: true)!;
            if (matches.Count > 1)
                throw Fail(lineInfo, $"<{name}> matches {matches.Count} converted types (keep one control per short name).");
        }
        // Slice-102: staged converted HandyControl controls (Watermark,
        // Drawer, SearchBar, ...) resolve from the converted lib image when
        // the entry has no same-named control; engine names and entry-local
        // controls keep winning above, so unrelated XAML is untouched.
        var handy = TryCreateConvertedHandyElement(name, namespaceUri, lineInfo);
        if (handy is not null) return handy;
        // clr-namespace elements (wpf:Ripple) bind the exact CLR name across
        // the loaded converted images; the declaring image is often a
        // staged vendor library, not the entry.
        var namespacedFallback = TryCreateClrNamespaceElement(name, namespaceUri, lineInfo);
        if (namespacedFallback is not null) return namespacedFallback;
        // Staged vendor visuals by short name (materialDesign:Ripple and
        // other converted controls under vendor xmlns tables): unique
        // UIElement match across the loaded converted images; engine names
        // and entry-local controls keep winning above.
        Type? scanned = null;
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            List<Type> candidates;
            try
            {
                candidates = WpfAppBoot.SafeGetTypes(assembly)
                    .Where(t => t.Name == name && typeof(UIElement).IsAssignableFrom(t) && !t.IsAbstract && HasDefaultCtor(t))
                    .ToList();
            }
            catch { continue; }
            foreach (var candidate in candidates)
            {
                if (scanned is not null && !ReferenceEquals(scanned, candidate))
                    throw Fail(lineInfo, $"<{name}> matches '{scanned.FullName}' and '{candidate.FullName}' (keep one converted control per short name).");
                scanned = candidate;
            }
        }
        if (scanned is not null)
            return (UIElement)Activator.CreateInstance(scanned, nonPublic: true)!;
        throw Fail(lineInfo, $"Unknown element <{name}>.");
    }

    /// <summary>
    /// Converted-app view-model shim (OxyPlot ExampleBrowser shape):
    /// a non-element tag nested in a DataContext property element
    /// is instantiated from the converted assembly via its default
    /// constructor so bindings flow. Returns false when the tag is not
    /// a resolvable plain-CLR type, letting the normal element path
    /// report it (UIElement tags keep element semantics; unknown tags
    /// keep the Unknown-element error).
    /// </summary>
    private static bool TryCreateDataContextValue(XmlReader reader, IXmlLineInfo lineInfo, out object? value)
    {
        value = null;
        var name = reader.LocalName;
        var source = CustomElementSource;
        if (source is null) return false;
        Type? candidate = null;
        if (name.Contains('.'))
        {
            try { candidate = source.GetType(name); }
            catch { candidate = null; }
        }
        else
        {
            var matches = WpfAppBoot.SafeGetTypes(source)
                .Where(t => t.Name == name && !t.IsAbstract &&
                            t.GetConstructor(
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance,
                                null, Type.EmptyTypes, null) is not null)
                .ToList();
            if (matches.Count > 1)
                throw Fail(lineInfo, $"<{name}> matches {matches.Count} converted types (keep one view-model per short name).");
            candidate = matches.Count == 1 ? matches[0] : null;
        }
        if (candidate is null) return false;
        if (typeof(UIElement).IsAssignableFrom(candidate)) return false;
        if (candidate.IsAbstract || candidate.GetConstructor(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance,
                null, Type.EmptyTypes, null) is null)
            throw Fail(lineInfo, $"<{name}> is not an instantiable view-model (needs a parameterless constructor).");
        value = ReadDataContextObject(reader, lineInfo, candidate);
        return true;
    }

    /// <summary>
    /// Consumes one view-model element (WPF constructs the object before
    /// setting properties): xmlns/design attributes are accepted and ignored,
    /// plain CLR attributes land on the instance, and anything else is a
    /// precise error. Only empty (or whitespace-only) content is supported.
    /// </summary>
    private static object ReadDataContextObject(XmlReader reader, IXmlLineInfo lineInfo, Type type)
    {
        var instance = Activator.CreateInstance(type, nonPublic: true)!;
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.Prefix is "d" or "mc" or "presentationOptions") continue;
                if (reader.Prefix == "x")
                    throw Fail(lineInfo, $"x:{reader.LocalName} is not supported on view-model <{type.Name}>.");
                if (!TrySetPlainObjectProperty(instance, reader.LocalName, reader.Value))
                    throw Fail(lineInfo, $"'{reader.LocalName}' is not a settable property on view-model <{type.Name}>.");
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return instance;
        }
        reader.Read();
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, $"<{type.Name}> takes no nested content in v1.");
        reader.Read();
        return instance;
    }

    /// <summary>Plain CLR property set for view-model attributes.</summary>
    private static bool TrySetPlainObjectProperty(object target, string local, string value)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var prop = target.GetType().GetProperty(local, flags);
        if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true) return false;
        if (prop.PropertyType == typeof(string) || prop.PropertyType == typeof(object))
        {
            prop.SetValue(target, value);
            return true;
        }
        try
        {
            var converted = StyleValueConverter.Convert(value, prop.PropertyType);
            if (converted is null || !prop.PropertyType.IsInstanceOfType(converted)) return false;
            prop.SetValue(target, converted);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static void MoveToContentElement(XmlReader reader, IXmlLineInfo lineInfo)
    {
        reader.MoveToContent();
        if (reader.EOF || reader.NodeType != XmlNodeType.Element)
            throw new XamlLoadException("Empty XAML document.");
    }

    private static void SkipWhitespace(XmlReader reader)
    {
        while (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) reader.Read();
    }

    private static XamlLoadException Fail(IXmlLineInfo lineInfo, string message) =>
        lineInfo.HasLineInfo()
            ? new XamlLoadException(message, lineInfo.LineNumber, lineInfo.LinePosition)
            : new XamlLoadException(message);

    /// <summary>
    /// Applies a `{...}` attribute value. v1 supports `{Binding [Path][, Mode=]
    /// [, UpdateSourceTrigger=][, ElementName=][, FallbackValue=][, TargetNullValue=]
    /// [, StringFormat=]}` on plain CLR properties; everything else is a precise error.
    /// </summary>
    private static void ApplyMarkupExtension(UIElement element, string local, string value, IXmlLineInfo lineInfo, List<PendingStatic> pending, Func<string, string?> xmlns)
    {
        if (!value.EndsWith('}'))
            throw Fail(lineInfo, $"Unterminated markup extension '{value}'.");
        var inner = value[1..^1].Trim();
        var head = inner.Split([' ', '\t', ','], 2)[0];
        if (head == "StaticResource")
        {
            // Deferred: attribute values resolve before the Parent link
            // exists, so ancestor lookup would spuriously fail.
            pending.Add(new PendingStatic(element, local, null, ParseStaticResourceKey(inner, value, lineInfo)));
            return;
        }
        if (head == "DynamicResource")
        {
            // Deferred: resolves-or-skips at attach plus refreshes on
            // resource/theme changes (missing keys stay unset, like WPF).
            if (element is not FrameworkElement dynamicTarget)
                throw Fail(lineInfo, $"Cannot apply DynamicResource '{local}' on <{element.GetType().Name}>.");
            var key = inner["DynamicResource".Length..].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                if (key.Length == 0 || key.Contains('='))
                    throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
                if (key.StartsWith("{x:Static", StringComparison.Ordinal))
                {
                    if (!key.EndsWith("}"))
                        throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
                    var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), value, lineInfo);
                    if (nested is ResourceKey plainKey &&
                        SystemTheme.TryFindSystemResource(plainKey, out var plainSystem) && plainSystem is not null &&
                        TrySetPropertyElementValue(element, local, plainSystem, lineInfo)) return;
                    if (nested is not ResourceKey)
                        throw Fail(lineInfo, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                    key = nested.ToString() ?? string.Empty;
                    if (key.Length == 0)
                        throw Fail(lineInfo, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                }
                else if (key.Contains('{'))
                    throw Fail(lineInfo, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
            pending.Add(new PendingStatic(dynamicTarget, local, null, key) { IsDynamic = true });
            return;
        }
        if (head == "TemplateBinding")
            throw Fail(lineInfo, "'{TemplateBinding ...}' is valid only inside a ControlTemplate (use {StaticResource ...} or {Binding ...} here).");
        if (head == "x:Null")
        {
            if (inner != "x:Null")
                throw Fail(lineInfo, "'{value}' takes no arguments ({{x:Null}} only).");
            if (TrySetPropertyElementValue(element, local, null, lineInfo)) return;
            throw Fail(lineInfo, "'{x:Null}' needs a settable reference property '" + local + "' on <" + element.GetType().Name + ">.");
        }
        if (head == "x:Static")
        {
            var memberText = inner["x:Static".Length..].Trim();
            object? resolvedMember;
            try
            {
                resolvedMember = ResolveStaticMember(memberText, value, lineInfo);
            }
            catch (XamlLoadException)
            {
                // Engine statics (SystemColors/Colors) keep winning; converted statics
                // (demo MessageToken string constants, lib statics) resolve like the
                // ex:Lang x:Static path, or stay loud here.
                resolvedMember = ResolveConvertedStatic(memberText, value, lineInfo, xmlns);
            }
            if (TrySetPropertyElementValue(element, local, resolvedMember, lineInfo)) return;
            throw Fail(lineInfo, "'" + value + "' needs a settable property '" + local + "' on <" + element.GetType().Name + ">.");
        }
        if (head.Contains(':') && TryApplyConvertedMarkupExtension(element, local, head, value, xmlns, lineInfo))
            return;
        if (head.Contains(':') && TryApplyConvertedExtensionObject(element, local, head, value, lineInfo))
            return;
        if (head != "Binding")
            throw Fail(lineInfo, $"'{{{head}}}' is not supported (only {{Binding ...}}, {{StaticResource ...}} and {{DynamicResource ...}}).");
        if (element is not FrameworkElement fe)
            throw Fail(lineInfo, $"Cannot bind '{local}' on <{element.GetType().Name}>.");
        try
        {
            if (local.Contains('.'))
                QueueApplicationBinding(fe, local, null, inner["Binding".Length..], value, lineInfo, pending);
            else
                QueueApplicationBinding(fe, local, inner["Binding".Length..], value, lineInfo, pending);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    internal static Binding ParseBinding(string args, string original, IXmlLineInfo lineInfo)
    {
        var binding = new Binding();
        var positional = true;
        foreach (var token in BindingOptions(args))
        {
            var part = token.Trim();
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim() == "StringFormat")
            {
                // StringFormat carries braces by design; the XAML '{}' escape
                // ('{}{0} Page' formats as "{0} Page") is stripped here.
                binding.StringFormat = ParseStringFormat(part[(eq + 1)..].Trim(), original, lineInfo);
                positional = false;
                continue;
            }
            // FallbackValue/TargetNullValue may carry {x:Static ...} (theme
            // bindings fall back to system values like Double.NaN); the
            // option readers below resolve them, everything else stays strict.
            var braceExempt = part.StartsWith("FallbackValue=", StringComparison.Ordinal)
                || part.StartsWith("TargetNullValue=", StringComparison.Ordinal)
                || part.StartsWith("Source=", StringComparison.Ordinal)
                || part.StartsWith("RelativeSource=", StringComparison.Ordinal);
            if (!braceExempt && (part.Contains('{') || part.Contains('}')))
                throw Fail(lineInfo, $"Nested markup extensions are not supported ('{original}').");
            if (eq < 0)
            {
                if (!positional)
                    throw Fail(lineInfo, $"Positional binding path must come first ('{original}').");
                positional = false;
                binding.Path = part;
                continue;
            }
            positional = false;
            var key = part[..eq].Trim();
            var val = part[(eq + 1)..].Trim();
            switch (key)
            {
                case "Path": binding.Path = val; break;
                case "Mode":
                    binding.Mode = val switch
                    {
                        "OneWay" => BindingMode.OneWay,
                        "OneTime" => BindingMode.OneTime,
                        "TwoWay" => BindingMode.TwoWay,
                        "Default" => BindingMode.Default,
                        "OneWayToSource" => BindingMode.OneWayToSource,
                        _ => throw Fail(lineInfo, $"'{val}' is not a binding mode (OneWay/OneTime/TwoWay)."),
                    };
                    break;
                case "ElementName": binding.ElementName = val; break;
                case "Source":
                    if (val.StartsWith("{x:Static ", StringComparison.Ordinal) && val.EndsWith('}'))
                    {
                        // Static sources (theme bindings read system values,
                        // like the HighContrast toolbar gate): evaluate now.
                        binding.Source = ResolveStaticMember(val[1..^1]["x:Static".Length..].Trim(), original, lineInfo);
                        break;
                    }
                    if (val.StartsWith("{StaticResource ", StringComparison.Ordinal) && val.EndsWith('}'))
                    {
                        // Resource sources defer to the target scope (like
                        // template bindings); the lookup boundary stays.
                        var sourceKey = val["{StaticResource ".Length..^1].Trim();
                        if (sourceKey.Length != 0 && !sourceKey.Contains('{') && !sourceKey.Contains('='))
                        {
                            binding.DeferredSourceKey = sourceKey;
                            break;
                        }
                    }
                    throw Fail(lineInfo, $"'{original}' takes Source as {{x:Static ...}} or {{StaticResource Key}} (other sources need resources/namescope lookup).");
                case "RelativeSource":
                    throw Fail(lineInfo, $"'{key}=' needs resources/namescope lookup (not in v1); set DataContext in code or use Source={{StaticResource Key}}.");
                case "UpdateSourceTrigger":
                    binding.UpdateSourceTrigger = val switch
                    {
                        "Default" => UpdateSourceTrigger.Default,
                        "PropertyChanged" => UpdateSourceTrigger.PropertyChanged,
                        "LostFocus" => UpdateSourceTrigger.LostFocus,
                        "Explicit" => UpdateSourceTrigger.Explicit,
                        _ => throw Fail(lineInfo, $"'{val}' is not an UpdateSourceTrigger (Default/PropertyChanged/LostFocus/Explicit)."),
                    };
                    break;
                case "FallbackValue": binding.FallbackValue = ParseNestedFallback(val, original, lineInfo); break;
                case "TargetNullValue": binding.TargetNullValue = ParseNestedFallback(val, original, lineInfo); break;
                case "StringFormat": binding.StringFormat = val; break;
                case "Converter":
                    throw Fail(lineInfo, "'Converter=' needs a resource lookup (not in v1); assign Binding.Converter in code.");
                case "ConverterParameter":
                    // Raw string, like template/BAML binding parameters;
                    // converters convert it (StringConverter precedent).
                    binding.ConverterParameter = val;
                    break;
                default: throw Fail(lineInfo, $"Unknown binding option '{key}' (Path/Mode/ConverterParameter/UpdateSourceTrigger/FallbackValue/TargetNullValue/StringFormat).");
            }
        }
        return binding;
    }

    /// <summary>
    /// Parses a StringFormat value: surrounding quotes and one leading XAML
    /// '{}' escape come off, format items stay for string.Format. Anything
    /// else brace-shaped is nested markup (precise error).
    /// </summary>
    /// <summary>Resolves a FallbackValue/TargetNullValue (literals pass
    /// through; {x:Static ...} resolves to the static value, like the
    /// theme bindings that fall back to system:Double.NaN).</summary>
    private static object ParseNestedFallback(string value, string original, IXmlLineInfo lineInfo)
    {
        var text = value.Trim();
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
            return ResolveStaticMember(text[1..^1]["x:Static".Length..].Trim(), original, lineInfo);
        return value;
    }

    private static string ParseStringFormat(string value, string original, IXmlLineInfo lineInfo)
    {
        var text = value;
        if (text.Length >= 2 && text.StartsWith('\'') && text.EndsWith('\''))
            text = text[1..^1];
        if (text.StartsWith("{}", StringComparison.Ordinal))
            return text[2..];
        if ((text.Contains('{') || text.Contains('}')) && !IsPlainFormatString(text))
            throw Fail(lineInfo, $"Nested markup extensions are not supported ('{original}').");
        return text;
    }

    /// <summary>Accepts literal text plus {index} placeholders ({{/}} escapes included); anything else stays nested markup.</summary>
    private static bool IsPlainFormatString(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { i += 2; continue; }
                var j = i + 1;
                while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
                if (j == i + 1) return false;
                if (j < text.Length && text[j] == ',')
                {
                    j++;
                    while (j < text.Length && text[j] == ' ') j++;
                    if (j < text.Length && text[j] == '-') j++;
                    var digits = j;
                    while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
                    if (j == digits) return false;
                }
                if (j < text.Length && text[j] == ':')
                {
                    j++;
                    while (j < text.Length && text[j] != '}') { if (text[j] == '{') return false; j++; }
                }
                if (j >= text.Length || text[j] != '}') return false;
                i = j + 1;
            }
            else if (ch == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}') { i += 2; continue; }
                return false;
            }
            else i++;
        }
        return true;
    }

    private static string ParseStaticResourceKey(string inner, string original, IXmlLineInfo lineInfo)
    {
        var key = inner["StaticResource".Length..].Trim();
        if (key.Length == 0)
            throw Fail(lineInfo, $"'{original}' needs a resource key ({{StaticResource Key}}).");
        if (key.Contains('{') || key.Contains('}'))
            throw Fail(lineInfo, $"Nested markup extensions are not supported ('{original}').");
        if (key.Contains('='))
            throw Fail(lineInfo, $"'{original}' takes a plain key only (ResourceKey=/Type= are not in v1).");
        return key;
    }

    /// <summary>
    /// Resolves {x:Static Type.Member} for engine statics and converted
    /// singletons (theme value converters like ShadowConverter.Instance):
    /// delegates to the shared engine-or-converted member resolution so
    /// loader paths see the same surface as code and style targets.
    /// </summary>
    private static object ResolveStaticMember(string member, string original, IXmlLineInfo lineInfo)
    {
    try { return ResolveStaticMember(member, lineInfo); }
    catch (XamlLoadException)
    {
    var dot = member.IndexOf('.');
    if (dot <= 0)
        throw Fail(lineInfo, "'" + original + "' needs a static member ({{x:Static Type.Member}}).");
    var typeName = member[..dot].Trim();
    var memberName = member[(dot + 1)..].Trim();
    var type = typeName switch
    {
        "SystemColors" => typeof(SystemColors),
        "Colors" => typeof(Colors),
        _ => null,
    };
    if (type is null)
        throw Fail(lineInfo, "'" + original + "' names an unsupported x:Static type '" + typeName + "' (SystemColors/Colors).");
    const System.Reflection.BindingFlags sflags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
    var prop = type.GetProperty(memberName, sflags);
    if (prop is not null && prop.GetIndexParameters().Length == 0 && prop.GetMethod?.IsPublic == true)
        return prop.GetValue(null) ?? throw Fail(lineInfo, "{original} resolved to null.");
    var field = type.GetField(memberName, sflags);
    if (field is not null)
        return field.GetValue(null) ?? throw Fail(lineInfo, "{original} resolved to null.");
    throw Fail(lineInfo, "'" + original + "' names an unknown static member '" + typeName + "." + memberName + ".");
    }
    }

    /// <summary>Parses a DoubleAnimation From/To/By (literals land now;
    /// {TemplateBinding Path} defers against the templated parent at
    /// storyboard run, like deferred durations).</summary>
    private static void ParseDoubleAnimationEndpoint(DoubleAnimation animation, string property, string value, IXmlLineInfo lineInfo)
    {
        var text = value.Trim();
        if (text.StartsWith("{TemplateBinding ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var path = text["{TemplateBinding ".Length..^1].Trim();
            if (path.Length == 0 || path.Contains('{') || path.Contains('='))
                throw Fail(lineInfo, $"'{value}' is not a TemplateBinding (needs a property name).");
            if (property == "From") animation.DeferredFromBinding = path;
            else if (property == "To") animation.DeferredToBinding = path;
            else animation.DeferredByBinding = path;
            return;
        }
        var parsed = ParseDouble(value, lineInfo);
        if (property == "From") animation.From = parsed;
        else if (property == "To") animation.To = parsed;
        else animation.By = parsed;
    }

    private static double ParseDouble(string value, IXmlLineInfo lineInfo)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        throw Fail(lineInfo, $"'{value}' is not a number.");
    }

    /// <summary>
    /// WPF LengthConverter parity for FrameworkElement extents: "Auto" maps
    /// to double.NaN (the unset/auto sentinel the layout passes already use).
    /// Every other length slot keeps strict ParseDouble behavior.
    /// </summary>
    private static double ParseLength(string value, IXmlLineInfo lineInfo)
    {
        if (string.Equals(value?.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        return ParseDouble(value, lineInfo);
    }

    private static int ParseInt(string value, IXmlLineInfo lineInfo)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        throw Fail(lineInfo, $"'{value}' is not an integer.");
    }

    private static bool ParseBool(string value, IXmlLineInfo lineInfo)
    {
        if (bool.TryParse(value, out var b)) return b;
        throw Fail(lineInfo, $"'{value}' is not a boolean (True/False).");
    }

    private static bool? ParseNullableBool(string value, IXmlLineInfo lineInfo)
    {
        if (bool.TryParse(value, out var b)) return b;
        throw Fail(lineInfo, $"'{value}' is not a boolean (True/False).");
    }

    private static Visibility ParseVisibility(string value, IXmlLineInfo lineInfo)
    {
        if (Enum.TryParse<Visibility>(value, out var v)) return v;
        throw Fail(lineInfo, $"'{value}' is not a Visibility (Visible/Hidden/Collapsed).");
    }

    private static NavigationUIVisibility ParseNavigationUIVisibility(string value, IXmlLineInfo lineInfo)
    {
        if (Enum.TryParse<NavigationUIVisibility>(value, out var v)) return v;
        throw Fail(lineInfo, $"'{value}' is not a NavigationUIVisibility (Automatic/Hidden).");
    }

    /// <summary>Local 0xRRGGBBAA convention (matches CanvasBridge Css and SkiaSurface).</summary>
    /// <summary>
    /// Brush resource entry with Opacity= and/or a deferred theme color
    /// (gallery acrylic tile brushes): plain opaque static colors keep the
    /// legacy packed-uint store; anything dynamic becomes a real
    /// SolidColorBrush resolved against current application resources.
    /// </summary>
    private static object ReadSolidColorBrushEntry(string? colorAttr, string? dynamicKey, string? opacityAttr, IXmlLineInfo lineInfo)
    {
        Color color;
        if (dynamicKey is not null)
            color = CoerceEntryColor(ResolveEntryDynamicValue(dynamicKey), dynamicKey, lineInfo);
        else
            color = StyleValueConverter.ColorFromRgba(ParseColor(colorAttr!, lineInfo));
        var opacity = 1.0;
        if (opacityAttr is not null)
        {
            if (!double.TryParse(opacityAttr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out opacity))
                throw Fail(lineInfo, $"'{opacityAttr}' is not an Opacity (0..1).");
            opacity = Math.Clamp(opacity, 0, 1);
        }
        if (dynamicKey is null && opacity >= 1)
            return StyleValueConverter.ColorFromRgba(ParseColor(colorAttr!, lineInfo));
        return new SolidColorBrush(color) { Opacity = opacity };
    }

    /// <summary>Resolves a resource-entry DynamicResource color key now (missing keys stay transparent).</summary>
    private static string ResolveEntryDynamicColor(string key, IXmlLineInfo lineInfo)
    {
        var resource = ResolveEntryDynamicValue(key);
        if (resource is null) return "#00000000";
        return resource switch
        {
            uint rgba => $"#{(rgba & 0xFF):X2}{(rgba >> 24) & 0xFF:X2}{(rgba >> 16) & 0xFF:X2}{(rgba >> 8) & 0xFF:X2}",
            Color color => color.ToString(),
            SolidColorBrush solid => solid.Color.ToString(),
            string text => text,
            _ => throw Fail(lineInfo, $"DynamicResource '{key}' is a {resource.GetType().Name}, not a color."),
        };
    }

    /// <summary>Resolves a resource-entry StaticResource color key now (missing keys fail like WPF).</summary>
    private static string ResolveEntryStaticColor(string key, IXmlLineInfo lineInfo)
    {
        object? resource;
        if (!TryFindHydrationResource(key, out resource) || resource is null)
            if (!(Application.Current?.Resources.TryGetValue(key, out resource) ?? false) || resource is null)
                throw Fail(lineInfo, $"StaticResource '{key}' was not found.");
        return resource switch
        {
            uint rgba => $"#{(rgba & 0xFF):X2}{(rgba >> 24) & 0xFF:X2}{(rgba >> 16) & 0xFF:X2}{(rgba >> 8) & 0xFF:X2}",
            Color color => color.ToString(),
            SolidColorBrush solid => solid.Color.ToString(),
            string text => text,
            _ => throw Fail(lineInfo, $"StaticResource '{key}' is a {resource.GetType().Name}, not a color."),
        };
    }

    /// <summary>Resolves a resource-entry effect Color extension (StaticResource strict, DynamicResource lenient).</summary>
    private static string ResolveEntryEffectColor(string raw, IXmlLineInfo lineInfo)
    {
        if (raw.StartsWith("{StaticResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            var key = raw["{StaticResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                throw Fail(lineInfo, $"'{raw}' takes a plain key (Color={{StaticResource Key}}).");
            return ResolveEntryStaticColor(key, lineInfo);
        }
        if (raw.StartsWith("{DynamicResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            var key = raw["{DynamicResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                throw Fail(lineInfo, $"'{raw}' takes a plain key (Color={{DynamicResource Key}}).");
            return ResolveEntryDynamicColor(key, lineInfo);
        }
        throw Fail(lineInfo, $"'{raw}' needs {{StaticResource Key}} or {{DynamicResource Key}}.");
    }

    private static object? ResolveEntryDynamicValue(string key)
    {
        var app = Application.Current;
        return app is not null && app.Resources.TryGetValue(key, out var resource) ? resource : null;
    }

    private static Color CoerceEntryColor(object? resource, string key, IXmlLineInfo lineInfo) =>
        resource switch
        {
            null => Colors.Transparent,
            uint rgba => StyleValueConverter.ColorFromRgba(rgba),
            Color color => color,
            SolidColorBrush solid => solid.Color,
            string text => StyleValueConverter.ColorFromRgba(ParseColor(text, lineInfo)),
            _ => throw Fail(lineInfo, $"DynamicResource '{key}' is a {resource.GetType().Name}, not a color."),
        };

    internal static uint ParseColor(string value, IXmlLineInfo lineInfo)
    {
        try { return ParseColorCore(value); }
        catch (ArgumentException ex) { throw Fail(lineInfo, ex.Message); }
    }

    /// <summary>Line-info-free color parsing for code-built scenes.</summary>
    public static uint ParseColor(string value)
    {
        try { return ParseColorCore(value); }
        catch (ArgumentException ex) { throw new XamlLoadException(ex.Message); }
    }

    internal static uint ParseColorCore(string value)
    {
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = value[1..];
            if ((hex.Length == 8 || hex.Length == 6) &&
                uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                if (hex.Length == 6) return (rgb << 8) | 0xFFu;
                // WPF #AARRGGBB -> internal 0xRRGGBBAA.
                return ((rgb & 0x00FFFFFFu) << 8) | (rgb >> 24);
            }

            throw new ArgumentException($"'{value}' is not a #RRGGBB or #AARRGGBB color.", nameof(value));
        }

        return value.ToLowerInvariant() switch
        {
            "transparent" => 0x00000000,
            "black" => 0x000000FF,
            "white" => 0xFFFFFFFF,
            "red" => 0xFF0000FF,
            "green" => 0x008000FF, // WPF Green is #FF008000 (dark), not Lime.
            "lime" => 0x00FF00FF, // WPF Lime is #FF00FF00 (bright).
            "blue" => 0x0000FFFF,
            "yellow" => 0xFFFF00FF,
            "gray" or "grey" => 0x808080FF,
            "darkgray" or "darkgrey" => 0xA9A9A9FF,
            "lightgray" or "lightgrey" => 0xD3D3D3FF,
            _ => KnownColor(value),
        };
    }

    private static uint KnownColor(string value)
    {
        var color = System.Drawing.Color.FromName(value);
        if (!color.IsKnownColor || color.IsSystemColor) throw new ArgumentException($"Unknown color '{value}'.", nameof(value));
        return ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | color.A;
    }
}

/// <summary>Roadmap marker: shaping backend status.</summary>
public static class WpfTextShaping
{
    public static string DescribePlan() =>
        "v3 shapes GlyphRun/FormattedText via SkiaSharp.HarfBuzz with embedded .ttf " +
        "fonts (TextShaper.RegisterEmbeddedFont), replayed in SkiaSurface with " +
        "shaped alignment; layout heuristics adopt shaped metrics next.";
}
