using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DesktopWasm.Wpf;

/// <summary>
/// BAML reader v2: parses real compiled WPF BAML (MS-BAML record stream) into
/// the engine visual tree. This is the browser counterpart to
/// System.Xaml/Baml2006Reader, which ships only in the Windows-only
/// WindowsDesktop pack and cannot run on browser-wasm.
///
/// Record framing (decoded against 15 real staged files: the HandyControl
/// skin/color/theme BAMLs plus one BAML each from the hello/gallery/
/// calculator/geometry/binding/oxyplot/pc-space-map packages, cross-checked
/// with the desktop Baml2006Reader node stream):
/// - header: I32 byte-length + UTF-16 "MSBAML", U16 major (0), U16 minor
///   (0x60), then a 15-byte trailer (00 00 60 00 00 00 60 00 01 00 FF FF FF
///   FF 00, byte-identical in every real file read so far) that v1 asserts
///   exactly: the earlier 14-byte skip never matched a real file (v1 only
///   ever parsed synthetic streams) and misread the trailer's last 0x00 as
///   record type BAML+0x22.
/// - fixed records: DocumentStart (0x01, 1 byte), ElementStart (0x03: U16
///   typeId + U8 flags), ElementEnd (0x04, 1 byte), LineNumberAndPosition
///   (0x35: U32 line + U32 pos), LinePosition (0x36: U32 pos),
///   ContentProperty (0x2E: I16 memberId), ConnectionId (0x2D: I32 LE
///   namescope id, validated then skipped),
///   DeferableContentStart (0x25: U32 skip-to-end distance, validated to land
///   on the dictionary's closing ElementEnd; the content itself is parsed,
///   never skipped).
/// - sized records: U8 type + 7-bit size (INCLUDING the size byte(s)) +
///   payload: AssemblyInfo (0x1C: U16 id + 7-bit-len ANSI name),
///   TypeInfo (0x1D: U16 id + U16 assemblyId + name),
///   AttributeInfo (0x1F: U16 id + I16 ownerTypeId + U8 flags + name),
///   StringInfo (0x20: U16 id + 7-bit-len ANSI string, the dictionary-key
///   table), StaticResourceId (0x26: U16 key-string id + U32 content offset +
///   U16 zero pad; the per-entry keys of a deferable dictionary, consumed in
///   order with the offset checked against the element stream),
///   PropertyWithConverter (0x24: I16 attrId + 7-bit-len ANSI value + I16
///   converterId), Text (0x10: 7-bit-len ANSI text), TextWithConverter
///   (0x11: 7-bit-len ANSI text + I16 converterId, the scalar dictionary
///   entry form), PIMapping/XmlnsProperty (namespace tables, skipped: v1
///   resolves everything through id tables).
/// - key/index framing (v2, decoded byte-exact against the staged 466350-byte
///   theme.baml and cross-checked against the ILSpy BamlDecompiler
///   BamlRecordType/BamlRecords sources, which mirror the MS-BAML wire
///   format): DefAttributeKeyString (0x26: U16 string-table value id + U32
///   content offset + 2xU8 shared flags; the v1 "zero pad" is the shared
///   pair), DefAttributeKeyType (0x27: ElementStart + U32 offset + shared
///   pair; implicit-style type keys), KeyElementStart (0x28: same layout;
///   element-form keys such as {x:Static ...}) with KeyElementEnd (0x29),
///   ConstructorParametersStart/End (0x2A/0x2B) with ConstructorParameterType
///   (0x2C: U16), OptimizedStaticResource (0x37: U8 flags + U16 value id;
///   fixed 3 content bytes, transparent inside the key index),
///   PropertyWithExtension (0x23: 3xU16), PropertyStringReference (0x21) and
///   PropertyTypeReference (0x22: complex-start U16 + U16),
///   PropertyWithStaticResourceId (0x38: 2xU16), StaticResourceStart (0x30:
///   ElementStart-shaped) with StaticResourceEnd (0x31), StaticResourceId
///   (0x32: U16), Property (0x05) and PropertyCustom (0x06: U16 attrId + U16
///   serializerId + bytes) and RoutedEvent (0x12) as sized records,
///   PropertyComplex/Array/Dictionary starts (0x07/0x09/0x0D: U16 attrId)
///   with their 1-byte ends, DefAttribute (0x19: x:Name/x:Key value),
///   TextWithId (0x33) and PresentationOptions (0x34: Freeze markers) as
///   sized records, NamedElementStart (0x2F: U16 typeId + 7-bit-len runtime
///   name, no size prefix). Line markers (0x35/0x36) and OptimizedStaticResource
///   records are transparent inside the key index (matching BamlDeferReader
///   SkipKeys); entry content offsets are validated against the defer
///   positions, so a misframed file fails loud instead of misaligning.
/// - id model: non-negative Int16 = per-file table index (0-based, in record
///   order); negative Int16 = well-known member/type/converter (negated
///   index into the framework known tables). Only the ids below are mapped;
///   anything else fails with a precise record-offset error.
///
/// v1 scope (HelloWorld-shaped content + TextBox + ScrollViewer + ListBox +
/// TreeView + DataGrid + items cluster, plus ResourceDictionary roots):
/// Window/Grid/TextBlock/TextBox/ScrollViewer/ListBox/ListBoxItem/TreeView/
/// TreeViewItem elements plus DataGrid/DataGridTextColumn and ItemsControl/
/// HeaderedItemsControl/ComboBox/ComboBoxItem/ListView/ListViewItem (which
/// have no well-known short ids and always travel by TypeInfo name),
/// Title/Icon/Height/Width/HorizontalAlignment/VerticalAlignment/TextBox.Text/
/// TextBlock.Text/ListBoxItem Content/ComboBoxItem Content/Selector.
/// SelectedIndex/ComboBox.Text+IsDropDownOpen+IsEditable/ItemsControl.
/// DisplayMemberPath/HeaderedContentControl.Header/TreeViewItem.IsExpanded/
/// DataGrid.Columns/DataGridColumn.Header/DataGridColumn.Width
/// members, String/Length/Enum/ImageSource/Bool/Uri/Color/UInt32/
/// DataGridLengthConverter converters, single text runs, property-list
/// records, ConnectionId + Name (namescope-only), and ResourceDictionary
/// roots with Source= merges (resolved by the caller through
/// XamlLoader.LoadBamlDictionary) plus keyed scalar entries (Color/UInt32,
/// Double, CornerRadius, Geometry, Thickness), keyed simple-setter Style
/// entries and empty converter/provider elements.
/// Keyed Style entries with a TargetType plus simple Setter values land as
/// live engine styles: Setter.Property travels as PropertyCustom (plain 137
/// short form with a well-known member id, 0x4000-flagged 16521 qualified
/// form with an owner type id plus the property name) and Setter.Value as a
/// PropertyWithConverter text converted through the DP type or a
/// PropertyCustom packed-ARGB brush value. Short-form Template, Focusable,
/// IsEnabled, HorizontalAlignment, VerticalAlignment,
/// HorizontalContentAlignment, VerticalContentAlignment, Background, Height,
/// Width, Fill, Visibility, BorderThickness, BorderBrush, Margin, Padding
/// (and Background on Border) resolve against the style TargetType
/// (Control.TemplateProperty,
/// even for converted targets; the rest walk the hierarchy so converted
/// targets land their inherited DPs) and complex Setter.Value content
/// holding one ControlTemplate or one Binding lands: a ControlTemplate
/// lands with its TargetType plus the single
/// template visual (ContentPresenter with a verbatim ContentSource string,
/// a Name joining the template namescope, TemplateBinding values recorded
/// on the template like the XAML loader path, or HorizontalAlignment/
/// VerticalAlignment/Visibility enum PWCs, or converted attached-DP PWCs,
/// or a StackPanel with per-file converted children carrying DP-store
/// simples plus constructor-arg Path/RelativeSource Binding values, or a
/// Border carrying Background/BorderBrush/BorderThickness TemplateBindings
/// with a named StackPanel child or a per-file Panel child carrying Margin/
/// alignment TemplateBindings with named ContentPresenter children; other
/// visuals stay precise follow-ups). ControlTemplate trigger lists land on
/// the template (Trigger elements with TargetName setters and x:Null
/// values) and style trigger lists land as Trigger/DataTrigger entries
/// (qualified or short-form Property with PropertyWithConverter values kept
/// raw, DataTrigger constructor-path Bindings with inline values, Setter
/// children through the same setter path). Implicit-style type keys resolve
/// to the entry CLR type without instantiating it and land under the Type
/// object. Styles with StaticResource/markup-extension values, other trigger
/// forms, data binding (incl. DataGridColumn.Binding markup extensions),
/// custom CLR types and x:Class codebehind are precise-error boundaries,
/// not silent gaps. The 466350-byte theme.baml loads its 950-key index, 87 scalar
/// entries (empty converter/provider elements plus
/// Color/UInt32/Double/CornerRadius/Geometry/Thickness text entries), its
/// first keyed Style entry ('PathBaseStyle': TargetType Path with
/// Stretch/SnapsToDevicePixels/FlowDirection setters), the x:Static-keyed
/// Double entry #93 (Double 30 under
/// SystemParameters.VerticalScrollBarButtonHeightKey, resolved to the engine
/// singleton), the converted Behavior entries #94-99 (live
/// HandyControl.Interactivity.FluidMoveBehavior values with AppliesTo/
/// Duration plus empty PowerEase EaseX/EaseY children, keyed x:Shared="False"
/// so every lookup materializes a fresh instance), the implicit style
/// entry #100 (TargetType BlendEffectBox with the Template setter holding
/// the ControlTemplate whose ContentPresenter carries
/// ContentSource="ActualContent"), the 'BorderCircular' entry #101
/// (TargetType Border with the converted
/// HandyControl.Controls.BorderElement.Circular attached setter landing
/// True through the converted DP), the implicit Col style entry #102
/// (TargetType HandyControl.Controls.Col with the short-form Focusable
/// setter landing False through the converted Focusable DP) and the
/// implicit ContentControl style entry #103 (same Focusable/False shape
/// over the well-known ContentControl target) and the implicit FlipClock
/// style entry #104 (Focusable/False plus the Template setter holding the
/// ControlTemplate whose StackPanel visual carries six converted FlipNumber
/// children with Margin plus Number Bindings over NumberList[0..5] with
/// TemplatedParent relative sources), the implicit FloatingBlock style entry
/// #105 (HorizontalAlignment/Left plus VerticalAlignment/Top plus the
/// Template setter holding the ControlTemplate whose ContentPresenter
/// carries the ContentTemplate TemplateBinding recorded on the template)
/// and the implicit GridSplitter style entry #106 (Focusable/False), the
/// implicit Notification style entry #107 (WindowAttach.IgnoreAltF4/
/// ShowInTaskManager plus Window-qualified SizeToContent/ResizeMode/
/// ShowActivated/ShowInTaskbar/Topmost plus short-form Background with a
/// packed-ARGB brush value plus the Template setter holding the
/// ControlTemplate whose ContentPresenter carries the Margin TemplateBinding
/// and Center alignment PWCs), the keyed 'RectangleCircular' style entry
/// (Rectangle plus the converted RectangleAttach.Circular setter), the
/// keyed 'SnapRect4ScreenshotStyle' style entry (Rectangle plus
/// short-form Height/Width/Fill plus HorizontalAlignment/VerticalAlignment
/// plus Visibility plus the Trigger list: eight TipElement.Placement-
/// qualified Triggers with enum values plus two IsSelecting/IsDrawing
/// DataTriggers with constructor-path Bindings, inline "True" values and
/// setters) and the keyed 'TargetArea4ScreenshotStyle' style entry over
/// Border (qualified UIElement.IsHitTestVisible plus short-form
/// BorderThickness/BorderBrush/Background plus two DataTriggers), then
/// the implicit SimpleItemsControl style entry (Template setter holding
/// the ControlTemplate whose Border visual carries Background/BorderBrush/
/// BorderThickness TemplateBindings with a PART_Panel StackPanel child)
/// and the implicit Sprite style entry (WindowAttach/Window setters plus
/// the Template setter holding the ControlTemplate whose ContentPresenter
/// carries the Margin TemplateBinding, the converted IsDragElement PWC and
/// Center alignment PWCs), then the nine transition Storyboard entries
/// keyidx 113-121 ('Right2LeftTransition' through 'FadeTransition':
/// Storyboard/DoubleAnimation resources with inline From/To, Duration,
/// TargetProperty and single-CubicEase EasingFunction children, keyed
/// x:Shared-free so every lookup materializes a fresh instance) and the
/// implicit TransitioningContentControl style entry (Focusable/False,
/// resolving to the engine type by short name), then the keyed
/// 'SliderRepeatButtonHorizontalStyle', 'SliderRepeatButtonVerticalStyle',
/// 'CompareSliderRepeatButtonTransparent' and 'ScrollBarBaseRepeatButton'
/// style entries (RepeatButton target with qualified FrameworkElement
/// OverridesDefaultStyle plus Background/Focusable/IsTabStop setters plus
/// the Template setter holding the ControlTemplate whose Border visual
/// carries a packed-ARGB Background plus Height/Width TemplateBindings with
/// a Rectangle child, or a bare ContentPresenter/Rectangle visual with
/// Content/Fill/Height/Width TemplateBindings), then the keyed
/// 'ToggleBlockBaseStyle' style entry (converted ToggleBlock target with
/// short-form VerticalContentAlignment/HorizontalContentAlignment plus
/// Focusable plus the Template setter holding the ControlTemplate whose
/// Border visual carries Background/BorderBrush/BorderThickness
/// TemplateBindings with a converted SimplePanel child carrying Margin/
/// VerticalAlignment/HorizontalAlignment TemplateBindings plus three named
/// ContentPresenters with ContentSource/Visibility values, plus the
/// ControlTemplate.Triggers list with three IsChecked Triggers carrying
/// TargetName setters and an x:Null value), then the keyed
/// 'GroupBoxBaseTopTemplate' entry (GroupBox target, Grid visual with
/// RowDefinitions plus two Borders carrying TemplateBinding and
/// TemplatedParent-bound values, landing as a live engine ControlTemplate),
/// then the keyed 'GroupBoxBaseLeftTemplate' entry (GroupBox target, Grid
/// visual with ColumnDefinitions plus two Borders carrying TemplateBinding
/// and TemplatedParent-bound values plus a LayoutTransform/TransformGroup/
/// RotateTransform chain, landing as a live engine ControlTemplate), then
/// the keyed 'GroupBoxTabTopTemplate' and 'GroupBoxTabLeftTemplate'
/// entries (GroupBox target, Grid visual with RowDefinitions/
/// ColumnDefinitions plus Borders carrying TemplateBinding and
/// TemplatedParent-bound values whose StaticResource Converters land as
/// deferred converter keys plus ConverterParameter simples, landing as
/// live engine ControlTemplates), then the four keyed ButtonGroup
/// ItemsPanelTemplate entries (UniformGrid Rows="1" / Columns="1" plus
/// StackPanel Orientation="Horizontal" / bare, each with FocusVisualStyle
/// {x:Null}, landing as live engine ItemsPanelTemplates retaining their
/// panel roots) plus the converted 'ButtonGroupItemStyleSelector' empty
/// element, then the keyed 'ProgressBarIndicatorAnimatedFill' brush entry
/// (LinearGradientBrush with four GradientStops, landing as a live engine
/// LinearGradientBrush), then the keyed 'CircleProgressBarTemplate' entry
/// (ControlTemplate over the converted CircleProgressBar with a converted
/// SimplePanel root, two converted Arc children and a TextBlock child whose
/// TemplatedParent Visibility Binding carries the Boolean2VisibilityConverter
/// static, landing as a live engine ControlTemplate with deferred converter
/// keys), then the four keyed Uniform/Stack ItemsPanelTemplate siblings,
/// then the two keyed VirtualizingStackPanel ItemsPanelTemplate siblings
/// (Orientation="Horizontal" / bare, each with FocusVisualStyle {x:Null},
/// landing as live engine ItemsPanelTemplates retaining their panel roots),
/// then the two keyed WrapPanel ItemsPanelTemplate siblings (the Center
/// HorizontalAlignment/VerticalAlignment pair plus Orientation="Vertical"
/// on the vertical entry) and the keyed DockPanel ItemsPanelTemplate
/// (LastChildFill), the keyed LinearGradientBrush and the keyed SineEase
/// (all landing as live engine objects), then the keyed
/// 'BadgeDotTemplate' ControlTemplate entry (converted SimplePanel root
/// with a bare ContentPresenter plus a Border carrying Width/Height/
/// CornerRadius simples, Margin/HorizontalAlignment/VerticalAlignment/
/// Background/BorderBrush/BorderThickness TemplateBindings and a
/// TemplateBinding-with-Converter Visibility transfer, landing as a live
/// engine ControlTemplate) and the keyed
/// 'ColorPickerSliderRepeatButtonBaseStyle' Style (RepeatButton target,
/// landing as a live engine Style), then stops at the keyed
/// 'ColorPickerSliderThumbTemplate' ControlTemplate entry (Thumb target
/// with a Border visual carrying Width/Height/Background/CornerRadius
/// simples, landing as a live engine ControlTemplate), then the keyed
/// 'ColorPickerRainbowBrush' LinearGradientBrush entry (no
/// StartPoint/EndPoint records, first stop Color-only: absent points
/// land at the WPF defaults and a missing Offset at 0.0), then the
/// keyed 'ColorPickerOpacityBrush' DrawingBrush entry (Viewport
/// '0,0,12,11' with Absolute units, Stretch None and TileMode Tile plus
/// a DrawingGroup of two GeometryDrawings over a two-rect GeometryGroup
/// and a single RectangleGeometry, landing as a live engine
/// DrawingBrush), then the keyed 'CoverFlowBaseStyle' Style entry
/// (converted CoverFlow target with Focusable False plus the Template
/// setter holding the ControlTemplate whose Border visual carries
/// Background/BorderBrush/BorderThickness TemplateBindings with the
/// PART_Viewport3D Viewport3D child: the PART_Camera PerspectiveCamera
/// through the Camera complex plus two DirectionalLight Content values
/// and PART_VisualParent in the Children list, landing as a live engine
/// Style), then the keyed 'CoverViewItemBaseStyle' Style entry
/// (converted CoverViewItem target with the same Focusable/Template
/// shape over the ContentSource Header ContentPresenter child, landing
/// as a live engine Style), then the keyed 'GridViewColumnHeaderGripper'
/// Style entry keyidx 157 (Thumb target with the qualified Canvas.Right
/// "-9" setter through the engine Canvas RightProperty, Width "18", the
/// Height ActualHeight TemplatedParent Binding, Padding "0", the
/// transparent packed-ARGB Background and the Template setter holding the
/// ControlTemplate whose Border visual carries the Padding TemplateBinding
/// through the engine Border.PaddingProperty, plus the Rectangle child
/// with its Background TemplateBinding and the Center/1 simples, landing
/// as a live engine Style), then the keyed 'SortByCategoryDrawingBrush'
/// DrawingBrush entry keyidx 158 and its sibling 'SortByNameDrawingBrush'
/// keyidx 159 (frozen DrawingGroups of frozen GeometryDrawings with
/// text-form brushes plus compact path-data geometries, landing as live
/// engine brushes), then the keyed 'TabItemMenu' ContextMenu entry
/// (three MenuItem rows binding HandyControl.Interactivity.ControlCommands
/// Close/CloseAll/CloseOther with HandyControl.Properties.Langs.Lang
/// Close/CloseAll/CloseOther headers, keyed x:Shared="False"): the rows
/// decode and the commands resolve live to the converted engine
/// RoutedCommands with live Lang headers (the converter attribute-scope fix),
/// then the keyed 'ToolBarTrayBaseStyle' Style entry (TargetType over
/// well-known id -672 with a single Background packed-ARGB setter, landing as
/// a live engine Style over the engine ToolBarTray, then the keyed 'TreeArrow'
/// PathGeometry entry (Figures string-form path data through the
/// PathFigureCollectionConverter, landing as a live engine PathGeometry),
/// then the keyed 'RowHeaderGripperStyle' Style entry (Thumb target with the
/// qualified Cursor setter plus the Height/Background short-form setters and
/// the Template setter, landing as a live engine Style) and its sibling
/// 'ColumnHeaderGripperStyle' Style entry (same shape with Cursor SizeWE),
/// then the keyed 'DataGridComboBoxColumnStyle' Style entry (ComboBox target
/// with the Focusable setter plus the Template setter holding the
/// ControlTemplate whose TextBlock visual carries IsHitTestVisible False,
/// the Text TemplateBinding and TextTrimming CharacterEllipsis, landing as a
/// live engine Style), then the keyed 'DataGridTextColumnStyle' Style entry
/// (TextBlock target with the short-form TextTrimming setter landing
/// CharacterEllipsis through TextBlock.TextTrimmingProperty, landing as a
/// live engine Style), then the keyed 'CarouselItemBaseStyle' Style entry
/// (converted CarouselItem target with the Focusable False setter plus the
/// Template setter holding the ControlTemplate whose Border visual carries
/// Background/BorderBrush/BorderThickness TemplateBindings with the
/// ContentPresenter child carrying the Margin TemplateBinding and the
/// ContentTemplate ItemTemplate Binding over the hc:Carousel AncestorType
/// FindAncestor RelativeSource, landing as a live engine Style), then the
/// implicit GrowlWindow style entry (converted GrowlWindow target with the
/// WindowAttach IgnoreAltF4/ShowInTaskManager setters plus the short-form
/// Width/MaxWidth setters landing 340 through the engine FrameworkElement
/// DPs plus short-form Background plus the Window-qualified ShowActivated/
/// ShowInTaskbar/Topmost setters, landing as a live engine Style);
/// everything after it: the keyed ListBoxItemCustom style entry (ListBoxItem
/// target id -374 with the SnapsToDevicePixels/FocusVisualStyle/Padding/
/// HorizontalContentAlignment/VerticalContentAlignment/Background/
/// BorderBrush/BorderThickness/Template setters, landing as a live engine
/// Style), then the implicit RibbonTab style entry (converted RibbonTab
/// target with the Focusable/VerticalContentAlignment short-form setters
/// plus the short-form ItemsPanel setter holding the ItemsPanelTemplate
/// complex with the StackPanel Orientation-Horizontal visual at BAML+0x1A92D,
/// plus the Template setter holding the ControlTemplate complex (the
/// converted SimplePanel PART_RootContainer root with its Visibility
/// Collapsed PWC at BAML+0x1A990 plus the mainBorder Border child plus
/// the ScrollViewer child at BAML+0x1A9DD (ScrollViewerAttach.Orientation
/// attached PWC plus Hidden/Disabled scrollbar simples plus the
/// ItemsPresenter child), landing as a live engine Style), then the
/// frozen DynamicResource-color SolidColorBrush run starting with the
/// keyed 'LightPrimaryBrush' entry (keyidx A4, well-known type id -578:
/// the Freeze marker plus the Color DynamicResource over the per-file
/// SolidColorBrush.Color row, resolving now against the dictionary-so-far
/// plus Application resources like the XAML loader resource-entry path,
/// landing as live engine SolidColorBrushes), then the frozen
/// DynamicResource-color LinearGradientBrush run starting with the keyed
/// 'PrimaryBrush' entry (keyidx 166, well-known type id -358: the Freeze
/// marker plus the EndPoint/StartPoint PWCs plus the GradientStops content
/// with GradientStop children carrying the Freeze marker plus the
/// DynamicResource Color over the per-file GradientStop.Color row,
/// resolving now like the SolidColorBrush run, landing as live engine
/// LinearGradientBrushes alongside the DynamicResource-color solid and
/// gradient siblings of the frozen brush run), then the frozen
/// StaticResource-color DropShadowEffect run (the five keyed
/// 'EffectShadow1' through 'EffectShadow5' entries at BAML+0x1AE8D:
/// the Freeze marker plus the BlurRadius/ShadowDepth/Direction/Opacity
/// PWCs plus the Color StaticResource over the per-file Color row,
/// resolving now against the earlier 'EffectShadowColor' Color entry,
/// landing as live engine DropShadowEffects), then the BasedOn/Data-static
/// Path style run (the fifteen keyed Path styles at BAML+0x1AFFD, from
/// 'SearchPathStyle' through 'LeftPathStyle', each carrying the BasedOn
/// StaticResource over the per-file Style.BasedOn row plus the short-form
/// Data PropertyCustom plus the Data StaticResource value, landing as live
/// engine Styles with apply-time base and geometry resolution), then the keyed
/// 'RightPathStyle' Style entry at BAML+0x1B219 (its qualified RenderTransformOrigin
/// PropertyCustom at BAML+0x1B22E resolves the engine UIElement.RenderTransformOriginProperty
/// DP with the "0.5, 0.5" PointConverter value, and its RenderTransform short-form
/// PropertyCustom at BAML+0x1B260 (member -134) resolves the engine
/// UIElement.RenderTransformProperty DP with the TransformGroup complex (ScaleTransform
/// ScaleY "1"/ScaleX "-1", SkewTransform AngleY/AngleX "0", RotateTransform Angle "0",
/// empty TranslateTransform), landing as a live engine Style whose apply sets the CLR
/// origin plus the mirror-flip group like the XAML loader path), then stops at the
/// qualified Border.CornerRadius setter at BAML+0x1B2F9 in style entry 'BorderRegion'
/// (owner Border is outside the routed Shape/UIElement/Window/FrameworkElement/Canvas/
/// ToolBarTray owner set, the next slice); the CarouselPateButton/implicit Carousel
/// styles, the GroupBox styles with BasedOn/DynamicResource setters and
/// TitleElement triggers, EventTriggers, richer complex properties and
/// BAML-tree template inflation) are follow-up work.
/// </summary>
public static class BamlReader
{
    // Well-known ids used by v1 (negated framework known-table indices,
    // verified against Baml2006SchemaContext for the pinned sample).
    private const short KnownWindow = -739;
    private const short KnownGrid = -254;
    private const short KnownTextBlock = -638;
    private const short KnownTextBox = -639; // verified against a real TextBox BAML fixture
    private const short KnownScrollViewer = -550; // verified against a real ScrollViewer BAML fixture
    private const short KnownListBox = -373; // verified against a real ListBox BAML fixture
    private const short KnownListBoxItem = -374; // verified against a real ListBox BAML fixture
    private const short KnownTreeView = -686; // verified against a real TreeView BAML fixture
    private const short KnownTreeViewItem = -687; // verified against a real TreeView BAML fixture
    private const short KnownMenu = -401; // verified against a real Menu BAML fixture
    private const short KnownMenuItem = -403; // verified against a real Menu BAML fixture
    private const short KnownSeparator = -554; // System.Windows.Controls.Separator (verified: the TestApp main window 0xB48 row)
    private const short KnownContextMenu = -105; // System.Windows.Controls.ContextMenu (verified: the staged HandyControl keyidx-160 'TabItemMenu' entry plus a genuine net10.0-windows fixture compiling the same ContextMenu/MenuItem block, both emitting 97 FF)
    private const short ConvCommand = -94; // CommandConverter (verified: the staged TabItemMenu MenuItem Command PropertyWithConverter values plus a genuine net10.0-windows fixture compiling MenuItem and Button Command properties with custom and framework statics, all emitting A2 FF)
    private const ushort ExtStaticExtension = 602; // StaticExtension as carried in PropertyWithExtension records (mirrors KnownStaticExtension = -602; verified: the staged TabItemMenu Header values plus the same genuine fixture, all emitting 5A 02)
    private const short KnownDockPanel = -163; // verified against a real DockPanel BAML fixture
    private const short KnownDock = -39; // DockPanel.Dock (verified against a real DockPanel BAML fixture)
    private const short KnownLastChildFill = -40; // DockPanel.LastChildFill (verified against a real DockPanel BAML fixture)
    private const short KnownContent = -14; // ContentControl.Content
    private const short KnownHeader = -71; // HeaderedContentControl.Header (verified against a real TreeView BAML fixture)
    private const short KnownItems = -192; // ItemsControl.Items (verified against a real ListBox BAML fixture)
    private const short KnownHeight = -47;
    private const short KnownHAlign = -48;
    private const short KnownVAlign = -56;
    private const short KnownWidth = -57;
    private const short KnownChildren = -213; // Panel.Children
    private const short KnownTextBoxText = -114; // TextBox.Text
    private const short KnownTextBlockText = -110; // TextBlock.Text (verified against a real ScrollViewer BAML fixture)
    private const short KnownName = -54; // FrameworkElement.Name (namescope-only in v1)
    private const short KnownIsEnabled = -133; // UIElement.IsEnabled (verified against a real Menu BAML fixture)
    private const short KnownResourceDictionary = -524; // ResourceDictionary (verified: root of the real skin/color/theme BAMLs)
    private const short KnownColor = -81; // System.Windows.Media.Color (verified: real colors BAML entries)
    private const short KnownUInt32 = -697; // System.UInt32 (verified: real colors BAML BlurGradientValue entry)
    private const short ConvString = -615; // StringConverter
    private const short ConvBool = -46; // BooleanConverter (verified against a real Menu BAML fixture)
    private const short ConvLength = -348; // LengthConverter (double)
    private const short ConvEnum = -195; // EnumConverter
    private const short ConvImage = -286; // ImageSourceConverter
    private const short ConvUri = -705; // UriConverter (verified: ResourceDictionary.Source in the real skin BAMLs)
    private const short ConvColor = -87; // ColorConverter (verified: real colors BAML entries)
    private const short ConvUInt32 = -698; // UInt32Converter (verified: real colors BAML BlurGradientValue entry)
    private const short KnownDouble = -168; // System.Double (verified: real theme BAML font sizes)
    private const short ConvDouble = -175; // DoubleConverter (verified: real theme BAML font sizes)
    private const short ConvDuration = -188; // DurationConverter (verified: staged theme.baml Behavior entries #94-99 carry Duration "0:0:.2"/"0:0:.4" with converter -188 on AttributeInfo 4 = FluidMoveBehavior.Duration)
    private const short KnownCornerRadius = -110; // System.Windows.CornerRadius (verified: real theme BAML corners)
    private const short ConvCornerRadius = -111; // CornerRadiusConverter (verified: real theme BAML corners)
    private const short KnownGeometry = -237; // System.Windows.Media.Geometry (verified: real theme BAML geometries)
    private const short ConvGeometry = -240; // GeometryConverter (verified: real theme BAML geometries)
    private const short ConvFontWeight = -222; // FontWeightConverter (verified: the genuine fw132 fixture compiling FontWeight="Bold" emits 22 FF)
    // PathGeometry keyed-entry run ids (verified: the staged HandyControl
    // keyidx-162 'TreeArrow' entry plus a genuine net10.0-windows fixture
    // compiling the upstream PathGeometry/Figures block, both emitting
    // 42 FE for the element, A7 FF for the member and 43 FE for the
    // converter, with byte-identical Figures text).
    private const short KnownPathGeometry = -446; // System.Windows.Media.PathGeometry
    private const short KnownFiguresMember = -89; // PathGeometry.Figures member
    private const short ConvPathFigures = -445; // PathFigureCollectionConverter
    private const short KnownBoolToVis = -49; // System.Windows.Controls.BooleanToVisibilityConverter (verified: real theme BAML entry)
    private const short KnownMenuScrolling = -404; // System.Windows.Controls.MenuScrollingVisibilityConverter (verified: real theme BAML entry)
    private const short KnownStyle = -620; // System.Windows.Style (verified: real theme BAML style surface)
    private const short KnownSetter = -556; // System.Windows.Setter (verified: real theme BAML style surface)
    private const short KnownPath = -442; // System.Windows.Shapes.Path (verified: Style TargetType of PathBaseStyle and 17 more, upstream TargetType="Path")
    private const short KnownShape = -558; // System.Windows.Shapes.Shape (verified: Setter.Property owner of Stretch; declares StretchProperty per net10 ref IL)
    private const short KnownUIElement = -694; // System.Windows.UIElement (verified: Setter.Property owner of SnapsToDevicePixels; declares it per net10 ref IL)
    private const short KnownFlowDirection = -46; // FrameworkElement.FlowDirection member (verified: short-form Setter.Property taking LeftToRight/RightToLeft in 2 staged styles; declares FlowDirectionProperty per net10 ref IL)
    private const short KnownStyleSetters = -238; // Style.Setters content member (verified: ContentProperty inside Style entries whose children are Setters)
    private const short KnownThickness = -651; // System.Windows.Thickness (verified: upstream <Thickness> DefaultControlPadding/DefaultInputPadding entries)
    private const short ConvThickness = -655; // ThicknessConverter (verified: same entries)
    private const short ConvGridLength = -256; // GridLengthConverter (verified: RowDefinition Height "Auto" in the genuine net10 fixture compiling the staged GroupBox template, byte-identical to the staged entry)
    private const short ConvInt32 = -315; // Int32Converter (verified: Grid.Row "1" in the same fixture, byte-identical to the staged entry)
    // PropertyCustom dependency-property serializer: plain 137 carries a
    // well-known member id; 0x4000-flagged 16521 carries an owner TypeInfo or
    // well-known type id plus the property name (both layouts byte-verified
    // against the staged theme.baml: 137/16521 share base id 137).
    private const short SerDependencyProperty = 137;
    private const ushort SerDependencyPropertyQualified = 16521;
    private const short KnownTrigger = -688; // System.Windows.Trigger (verified: real theme BAML style surface)
    private const short KnownControlTemplate = -108; // System.Windows.Controls.ControlTemplate (verified: real theme BAML)
    private const short KnownBinding = -20; // System.Windows.Data.Binding (verified: real theme BAML style surface)
    private const short KnownStaticExtension = -602; // System.Windows.Markup.StaticExtension (verified: real theme BAML x:Static keys)
    private const short KnownBorder = -50; // System.Windows.Controls.Border (verified: Style TargetType of the staged BorderCircular entry, upstream TargetType="Border")
    private const short KnownContentControl = -100; // System.Windows.Controls.ContentControl (verified: TargetType of the staged #103 implicit style plus a real net10 ContentControl BAML fixture)
    private const short KnownContentPresenter = -102; // System.Windows.Controls.ContentPresenter (verified: single template child of the staged BlendEffectBox entry, upstream <ContentPresenter ContentSource="ActualContent" />)
    private const short KnownItemsPresenter = -331; // System.Windows.Controls.ItemsPresenter (verified: the staged RibbonTab ScrollViewer ItemsPresenter child at BAML+0x1AA11 plus a genuine net10.0-windows fixture compiling the same ScrollViewer/ItemsPresenter block, both emitting B5 FE)
    private const short KnownSolidColorBrush = -578; // System.Windows.Media.SolidColorBrush (verified: the staged HandyControl keyidx-A4 'LightPrimaryBrush' entry at BAML+0x1AA1C plus a genuine net10.0-windows fixture compiling DynamicResource/literal-color SolidColorBrush entries, both emitting BE FD)
    // DynamicResource PropertyWithExtension discriminator (verified: the staged LightPrimaryBrush-run 0x23 Color values plus the genuine net10.0-windows fixture compiling Color="{DynamicResource ...}" on a SolidColorBrush and a GradientStop, both emitting BD 00 with the resource key as a StringInfo id).
    private const ushort ExtDynamicResource = 189;
    private const short KnownStackPanel = -601; // System.Windows.Controls.StackPanel (verified: template visual of the staged #104 FlipClock style - Orientation/Horizontal with six FlipNumber children - plus a real net10 StackPanel BAML fixture; name-only until template inflation lands)
    private const short KnownDataTemplate = -120; // System.Windows.DataTemplate (verified: genuine net10.0-windows fixture C:\Temp\fw120 compiling a Window.Resources DataTemplate emits 88 FF at the element head; real TestApp mainwindow.baml DocumentHeaderTemplate root at BAML+0x13B9 emits the same id)
    private const short KnownImage = -281; // System.Windows.Controls.Image (verified: genuine net10.0-windows fixture E:\tmp\fw121 - DataTemplate/StackPanel/Orientation/Image-16px/Margin/Source-Binding/Style-Trigger/TextBlock-Title template mirroring the real shape - emits the E7 FE element head; real TestApp mainwindow.baml emits the byte-identical head for the Width/Height-16 Margin Source-{Binding IconSource} child inside the DocumentHeaderTemplate StackPanel.Children at BAML+0x13EE, modulo per-file table numbering and line markers)
    private const short KnownImageSourceMember = -75; // Image.Source complex member (verified: the same genuine fixture carries the Source {Binding IconSource} value as the 07 B5 FF complex child on the -281 Image; the real TestApp file carries the byte-identical member at BAML+0x140F)
    private const short KnownStyleMember = -55; // FrameworkElement.Style complex member (verified: the same genuine fixture carries the inline Image.Style value as the 07 C9 FF complex child holding the -620 Style; the real TestApp file carries the byte-identical member at BAML+0x1423)
    private const short KnownButton = -55; // System.Windows.Controls.Button (verified: genuine net10.0-windows fixture E:\\tmp\\fw123 compiling Label/Button/CheckBox/RadioButton/Slider/ProgressBar/Expander/GroupBox/Viewbox/Frame/StatusBar/Canvas/TextBlock emits exactly one 03 C9 FF head wrapping the probe-button text; real TestApp mainwindow.baml carries the byte-identical element head at BAML+0x1BCA)
    // (KnownTextTrimmingMember = -112 already declared below with the staged/fixture verification; the TestApp DocumentHeaderTemplate title carries the same 90 FF simple at BAML+0x1474, and the generic ApplyProperty switch gains its arm alongside Margin.)
    private const short KnownVirtualizingStackPanel = -732; // System.Windows.Controls.VirtualizingStackPanel (verified: a genuine net10.0-windows fixture compiling a VirtualizingStackPanel Orientation/Horizontal ItemsPanelTemplate emits 24 FD, byte-identical in every staged record shape to the staged keyidx 143-144 visuals modulo per-file table numbering and line markers, verified against a genuine net10.0-windows fixture compiling the upstream VirtualizingStackPanel Orientation/Horizontal plus bare ItemsPanelTemplates (the fixture emits the 24 FD head plus the Orientation EnumConverter simple and the FocusVisualStyle x:Null complex); the panel lands as a live engine VirtualizingStackPanel)
    private const short KnownWrapPanel = -742; // System.Windows.Controls.WrapPanel (verified: the staged HandyControl keyidx 145-146 Wrap items-panel entries, emitting 1A FD with the Orientation/HorizontalAlignment/VerticalAlignment simples plus the FocusVisualStyle x:Null complex; owns the per-file Orientation row 54; the panel lands as a live engine WrapPanel)
    private const short KnownItemsPanelTemplate = -330; // System.Windows.Controls.ItemsPanelTemplate (verified: the staged HandyControl keyidx 132-135 ButtonGroup entries plus a genuine net10.0-windows fixture compiling the upstream ButtonGroupBaseStyle ItemsPanelTemplates, both emitting B6 FE)
    private const short KnownLinearGradientBrush = -358; // System.Windows.Media.LinearGradientBrush (verified: the staged HandyControl keyidx 137 'ProgressBarIndicatorAnimatedFill' entry plus a genuine net10.0-windows fixture compiling the upstream ProgressBarBaseStyle block, both emitting 9A FE)
    private const short KnownGradientStop = -252; // System.Windows.Media.GradientStop (verified: GradientStop children of the same staged entry plus the fixture, both emitting 04 FF)
    // GradientBrush.GradientStops content member (verified: the 2E C4 FF
    // content in the same staged entry plus the fixture, which the
    // compiler emits for the implicit GradientStops content property).
    private const short KnownGradientStopsContent = -60;
    // DropShadowEffect entries travel as per-file TypeInfo rows (verified: the staged HandyControl EffectShadow1-5 entries ride per-file TypeInfo 63 naming System.Windows.Media.Effects.DropShadowEffect over PresentationCore 10.0.0.0, plus a genuine net10.0-windows fixture compiling the upstream Themes/Basic/Effects.xaml block, whose effect elements ride per-file TypeInfo 0 over the same framework name).
    private const string DropShadowEffectTypeName = "System.Windows.Media.Effects.DropShadowEffect";
    private const short KnownUniformGrid = -703; // System.Windows.Controls.Primitives.UniformGrid (verified: panel visual of the same staged entries plus the fixture, both emitting 41 FD; owns the per-file Rows/Columns rows)
    // DrawingBrush run ids (verified: the staged HandyControl keyidx 154
    // 'ColorPickerOpacityBrush' entry plus a genuine net10.0-windows fixture
    // compiling the upstream ColorPickerBaseStyle block, both emitting 4C FF
    // for the brush, 49 FF for the DrawingGroup, 0F FF for the
    // GeometryDrawings, 0E FF for the GeometryGroup, FD FD for the
    // RectangleGeometries, D6 FF for the DrawingGroup.Children content, C5 FF
    // for the GeometryGroup.Children content and 6A FD for the TileBrush
    // Viewport/ViewportUnits/Stretch/TileMode rows).
    private const short KnownDrawingBrush = -180; // System.Windows.Media.DrawingBrush
    private const short KnownDrawingGroup = -183; // System.Windows.Media.DrawingGroup
    private const short KnownGeometryDrawing = -241; // System.Windows.Media.GeometryDrawing
    private const short KnownGeometryGroup = -242; // System.Windows.Media.GeometryGroup
    private const short KnownRectangleGeometry = -515; // System.Windows.Media.RectangleGeometry
    private const short KnownTileBrush = -662; // System.Windows.Media.TileBrush (owner of the staged/fixture Viewport rows)
    private const short KnownDrawingChildren = -42; // DrawingGroup.Children content member
    private const short KnownGeometryChildren = -59; // GeometryGroup.Children content member
    private const short ConvRect = -511; // RectConverter (verified: staged/fixture Viewport "0,0,12,11" plus Rect "0,0,6,5" PWCs, emitting 01 FE)
    // Well-known member ids (a separate table from the type ids above, so
    // the values below may coincide with type ids like KnownBinding).
    private const short KnownTemplateMember = -37; // Template member (verified: short-form Setter.Property of the staged BlendEffectBox entry, upstream Property="Template")
    private const short KnownFocusableMember = -132; // Focusable member (verified: short-form Setter.Property of Property="Focusable" in a real net10 BAML fixture, adjacent to IsEnabled -133)
    private const short KnownIsEnabledMember = -133; // IsEnabled member (verified: short-form Setter.Property of Property="IsEnabled" with "False"/BooleanConverter in the same real net10 BAML fixture)
    private const short KnownHAlignMember = -48; // HorizontalAlignment member (verified: short-form Setter.Property of Property="HorizontalAlignment" with "Left"/EnumConverter in the same real net10 BAML fixture; staged #105 carries it)
    private const short KnownVAlignMember = -56; // VerticalAlignment member (verified: short-form Setter.Property of Property="VerticalAlignment" with "Top"/EnumConverter in the same real net10 BAML fixture; staged #105 carries it)
    private const short KnownPanelBackgroundMember = -86;
    private const short KnownTextBoxBorderThicknessMember = -26; // TextBox.BorderThickness member (verified: genuine fw125 TextBox BorderThickness="0" fixture emits the byte-identical 24 07 E6 FF 01 30 71 FD record; the real TestApp file carries it at BAML+0x1C98).
    private const short KnownTextBoxBackgroundMember = -24; // TextBox.Background member (verified: genuine fw124 TextBox Background="Transparent" fixture emits the byte-identical 06 0A E8 FF E8 02 01 FF FF FF 00 record; the real TestApp file carries it at BAML+0x1C12). // Panel.Background member (verified: genuine fw122 Background="Transparent" fixture emits the byte-identical in-element 0x06 record; the real TestApp file carries it at BAML+0x1BA3/0x1F49/0x22E9).
    private const short KnownBackgroundMember = -24; // Background member (verified: short-form Setter.Property of Property="Background" with a 744 packed-ARGB brush value in the staged #107 shape plus a real net10 Background/StaticResource BAML fixture)
    private const short KnownHeightMember = -47; // Height member (verified: short-form Setter.Property of Property="Height" with "5"/LengthConverter in the staged SnapRect shape plus the same fixture)
    private const short KnownWidthMember = -57; // Width member (verified: short-form Setter.Property of Property="Width" with "5"/LengthConverter in the same shapes)
    private const short KnownFillMember = -100; // Fill member (verified: short-form Setter.Property of Property="Fill" with a 744 packed-ARGB brush value in the same shapes)
    private const short KnownStrokeMember = -101; // Shape.Stroke member (verified: {TemplateBinding BorderBrush} target on the staged CircleProgressBar Arc plus a genuine net10.0-windows fixture compiling the same TemplateBinding on a Rectangle, both emitting 9B FF)
    private const short KnownTextBlockForegroundMember = -109; // TextBlock.Foreground member (verified: {TemplateBinding Foreground} target on the staged CircleProgressBar TextBlock plus the same genuine fixture compiling it on a TextBlock, both emitting 93 FF)
    private const short KnownVisibilityMember = -135; // Visibility member (verified: short-form Setter.Property of Property="Visibility" with "Collapsed"/EnumConverter in the staged SnapRect shape plus the same fixture)
    // RenderTransform short-form setter member (verified: short-form Setter.Property of Property="RenderTransform"
    // with a TransformGroup complex value in the staged RightPathStyle entry plus a genuine net10.0-windows
    // fixture compiling the upstream Path style block, both emitting 7A FF over the Setter.Property row).
    private const short KnownRenderTransformMember = -134;
    // Packed-ARGB brush value serializer (a PropertyCustom value record whose
    // serializer id is 744 and whose 5 payload bytes are 0x01 plus the LE
    // ARGB quad: fixture-verified Background="Red" as 01 0000FFFF, byte-
    // identical in shape to the staged Transparent 01 FFFFFF00 and DodgerBlue
    // 01 FF901EFF values).
    private const ushort BrushValueSerializer = 744;
    // Compact path-data value serializer (verified: the staged
    // SortByCategory brush carries Geometry values with serializer 746
    // and the genuine net10 fixture compiling the same upstream block
    // emits the identical id with payloads WPF's own
    // XamlPathDataSerializer decodes to the exact staged figures).
    private const ushort PathDataSerializer = 746;
    private const short KnownGridSplitter = -257; // System.Windows.Controls.GridSplitter (verified: TargetType of the staged #106 implicit style plus a real net10 GridSplitter BAML fixture)
    private const short KnownRectangle = -514; // System.Windows.Shapes.Rectangle (verified: TargetType of the staged 'SnapRect4ScreenshotStyle' style plus a real net10 Rectangle BAML fixture, both emitting FE FD)
    private const short KnownRepeatButton = -522; // System.Windows.Controls.RepeatButton (verified: TargetType of the staged 'SliderRepeatButtonHorizontalStyle' style entries plus a real net10 RepeatButton BAML fixture, both emitting F6 FD)
    private const short KnownGroupBox = -264; // System.Windows.Controls.GroupBox (verified: TargetType of the staged 'GroupBoxBaseTopTemplate' entry plus a genuine net10 fixture compiling the same template, both emitting F8 FE)
    private const short KnownThumb = -658; // System.Windows.Controls.Primitives.Thumb (verified: TargetType of the staged 'ColorPickerSliderThumbTemplate' entry plus a genuine net10 fixture compiling the same template, both emitting 6E FD)
    private const short KnownToolBarTray = -672; // System.Windows.Controls.ToolBarTray (verified: TargetType of the staged keyidx-161 'ToolBarTrayBaseStyle' entry plus a genuine net10.0-windows fixture compiling the upstream ToolBarTrayBaseStyle block, both emitting 60 FD with the qualified Background packed-ARGB setter over owner -672)
    private const short KnownComboBox = -92; // System.Windows.Controls.ComboBox (verified: TargetType A4 FF of the staged keyidx-165 'DataGridComboBoxColumnStyle' entry at BAML+0x1A10F plus the staged per-file Text row 81 owned by A4 FF plus a genuine net10.0-windows fixture compiling the exact upstream DataGridBaseStyle block, byte-identical in every staged record shape modulo per-file numbering and line markers)
    private const short KnownCanvas = -66; // System.Windows.Controls.Canvas (verified: qualified Setter.Property owner of Canvas.Right in the staged keyidx-157 'GridViewColumnHeaderGripper' entry plus a genuine net10.0-windows fixture compiling the same style, both emitting BE FF)
    private const short KnownRowDefinition = -541; // System.Windows.Controls.RowDefinition (verified: RowDefinitions children of the staged GroupBox template plus the same genuine net10 fixture, both emitting E3 FD)
    private const short KnownFrameworkElement = -226; // System.Windows.FrameworkElement (verified: owner of the staged OverridesDefaultStyle/ActualHeight/ActualWidth rows plus a real net10 fixture compiling OverridesDefaultStyle="true", all emitting 1E FF)
    private const short KnownIsTabStopMember = -34; // IsTabStop member (verified: short-form Setter.Property of Property="IsTabStop" with "false"/BooleanConverter in a real net10 fixture compiling the staged RepeatButton styles, emitting DE FF)
    private const short KnownStrokeThicknessMember = -102; // Shape.StrokeThickness member (verified: template PWC of StrokeThickness="0" with LengthConverter in a real net10 fixture compiling the staged RepeatButton templates, emitting 9A FF)
    private const short KnownPresenterContentMember = -19; // ContentPresenter.Content member (verified: TemplateBinding target of Content="{TemplateBinding Content}" in a real net10 fixture compiling the staged CompareSlider template, emitting ED FF)
    private const short KnownHContentMember = -33; // Control.HorizontalContentAlignment member (verified: short-form Setter.Property of Property="HorizontalContentAlignment" with "Center"/EnumConverter in a genuine net10 fixture compiling the staged ToggleBlock shapes, emitting DF FF)
    private const short KnownVContentMember = -38; // Control.VerticalContentAlignment member (verified: short-form Setter.Property of Property="VerticalContentAlignment" with "Center"/EnumConverter in the same fixture, emitting DA FF)
    private const short KnownNullExtension = -422; // System.Windows.Markup.NullExtension (verified: complex Trigger.Value child in the same fixture compiling Value="{x:Null}", emitting 5A FE)
    // GroupBox template members (verified: a genuine net10.0-windows fixture
    // compiling the exact staged 'GroupBoxBaseTopTemplate' XAML, byte-identical
    // in every record shape to the staged entry modulo per-file table
    // numbering; the staged texts resolve through the upstream
    // GroupBoxBaseStyle.xaml, e.g. Border MinHeight bound to
    // (hc:TitleElement.MinHeight) with a TemplatedParent RelativeSource).
    private const short KnownGridRowMember = -63; // Grid.Row attached member (verified: PWC "1"/Int32Converter on the staged second Border plus the fixture, emitting C1 FF)
    private const short KnownMinHeightMember = -52; // FrameworkElement.MinHeight member (verified: complex Binding child on the staged first Border plus the fixture, emitting CC FF)
    private const short KnownRecognizesAccessKeyMember = -23; // ContentPresenter.RecognizesAccessKey member (verified: PWC "True"/BooleanConverter on the staged header presenter plus the fixture, emitting E9 FF)
    private const short KnownTextForegroundMember = -121; // TextElement.Foreground member (verified: {TemplateBinding Foreground} target on the staged header presenter plus the fixture, emitting 87 FF)
    private const short KnownForegroundSourceMember = -32; // Control.Foreground source (verified: the same {TemplateBinding Foreground} source plus the fixture, emitting E0 FF)
    private const short KnownRowHeightMember = -94; // RowDefinition.Height member (verified: PWC "Auto"/GridLengthConverter on the staged first RowDefinition plus the fixture, emitting A2 FF)
    // GroupBox LeftTemplate members (verified: a genuine net10.0-windows
    // fixture compiling the exact staged 'GroupBoxBaseLeftTemplate' shapes -
    // ColumnDefinitions with Width="Auto", Grid.Column="1",
    // ClipToBounds="False", RenderTransformOrigin="0.5,0.5" and a
    // LayoutTransform/TransformGroup/RotateTransform Angle="-90" chain -
    // byte-identical in every staged record shape to the staged entry modulo
    // per-file table numbering; the staged texts resolve through the upstream
    // GroupBoxBaseStyle.xaml).
    private const short KnownColumnDefinition = -90; // System.Windows.Controls.ColumnDefinition (verified: ColumnDefinitions children of the staged LeftTemplate plus the fixture, both emitting A6 FF)
    private const short KnownColumnWidthMember = -13; // ColumnDefinition.Width member (verified: PWC "Auto"/GridLengthConverter on the staged first ColumnDefinition plus the fixture, emitting F3 FF)
    private const short KnownGridColumnMember = -61; // Grid.Column attached member (verified: PWC "1"/Int32Converter on the staged second Border plus the fixture, emitting C3 FF)
    private const short KnownClipToBoundsMember = -131; // UIElement.ClipToBounds member (verified: PWC "False"/BooleanConverter on the staged first Border plus the fixture, emitting 7D FF)
    private const short KnownTransformGroup = -682; // System.Windows.Media.TransformGroup (verified: LayoutTransform child on the staged header presenter plus the fixture, both emitting 56 FD)
    private const short KnownTransformChildren = -130; // TransformGroup.Children member (verified: content member inside the staged TransformGroup plus the fixture, emitting 7E FF)
    private const short KnownRotateTransform = -528; // System.Windows.Media.RotateTransform (verified: TransformGroup child on the staged header presenter plus the fixture, both emitting F0 FD; the same id owns the per-file Angle row: staged row 36 plus the fixture row 4)
    // Style-setter TransformGroup child types (verified: the staged RightPathStyle RenderTransform complex plus
    // a genuine net10.0-windows fixture compiling the same TransformGroup block - ScaleTransform DE FD,
    // SkewTransform C1 FD, RotateTransform F0 FD, TranslateTransform 54 FD - byte-identical in every record
    // shape modulo per-file table numbering and the line markers the staged file omits).
    private const short KnownScaleTransform = -546; // System.Windows.Media.ScaleTransform
    private const short KnownSkewTransform = -575; // System.Windows.Media.SkewTransform
    private const short KnownTranslateTransform = -684; // System.Windows.Media.TranslateTransform
    private const short ConvPoint = -474; // PointConverter (verified: PWC "0.5,0.5" on the staged RenderTransformOrigin plus the fixture, emitting 26 FE)
    private const short KnownMinWidthMember = -53; // FrameworkElement.MinWidth member (verified: the same LeftTemplate fixture emitting CB FF for MinWidth="13" plus the staged 07 CB FF MinWidth TemplatedParent binding)
    private const short KnownTextWrappingMember = -113; // TextBlock.TextWrapping member (verified: the genuine fw131 fixture compiling TextWrapping="WrapWithOverflow" emits 8F FF with EnumConverter)
    private const short KnownFontWeightMember = -108; // TextBlock.FontWeight member (verified: the genuine fw132 fixture compiling FontWeight="Bold" emits 94 FF with converter -222)
    private const short KnownMaxWidthMember = -51; // FrameworkElement.MaxWidth member (verified: short-form Setter.Property of Property="MaxWidth" with "340"/LengthConverter in the staged GrowlWindow entry plus a genuine net10.0-windows fixture compiling the same Width/MaxWidth shapes on a Window, both emitting CD FF with the A4 FE value)
    private const short KnownItemsPanelMember = -81; // ItemsControl.ItemsPanel member (verified: short-form Setter.Property of Property="ItemsPanel" with an ItemsPanelTemplate complex value in the staged RibbonTab entry plus a genuine net10.0-windows fixture compiling the same Focusable/VerticalContentAlignment/ItemsPanel shape on a TabControl style, both emitting AF FF; shares its number with the KnownColor *type* id, but members and types are separate well-known namespaces like the -122 trigger precedent)
    private const short KnownDataTrigger = -122; // System.Windows.DataTrigger (verified: the staged SnapRect/TargetArea trigger lists plus a real net10 fixture compiling DataTrigger/Binding/Value/Setters, all emitting 86 FF)
    // Trigger/DataTrigger Setters content members (verified: a real net10
    // fixture compiling Trigger and DataTrigger styles emits 00 FF for the
    // Trigger Setters content and 61 FF for the DataTrigger one,
    // byte-identical to the staged SnapRect/TargetArea lists).
    // Path.Data member (verified: short-form Setter.Property of Property="Data" with a StaticResource Geometry value in the staged SearchPathStyle entry plus a genuine net10.0-windows fixture compiling the upstream Themes/Basic/Paths.xaml block, both emitting A9 FF; shares its number with the ConvColor *converter* id, but members and converters are separate well-known namespaces like the -81 member precedent).
    private const short KnownDataMember = -87;
    private const short KnownTriggerSetters = -256;
    private const short KnownDataTriggerSetters = -159;
    // Border short-form setter members (verified: a real net10 fixture
    // compiling a Border-targeted style emits FA FF for BorderThickness
    // ("5"/ThicknessConverter), FB FF for BorderBrush (#AARRGGBB text) and
    // FC FF for Background (White travels packed-ARGB like the staged TargetArea
    // values). Background also travels as -24 on Control-derived targets
    // (the staged #107 Notification shape): the member names the declaring
    // site, resolution walks the style TargetType either way.
    private const short KnownBorderThicknessMember = -6;
    private const short KnownBorderBrushMember = -5;
    private const short KnownBorderBackgroundMember = -4;
    // Border.Child content member (verified: the Decorator.Child content
    // inside the staged SimpleItemsControl Border template visual plus the
    // real net10 Border/TemplateBinding BAML fixture, all emitting 5F FF
    // for the Border content).
    private const short KnownBorderChildContent = -161;
    // Viewport3D template-visual ids (verified: the staged theme.baml
    // keyidx-155 'CoverFlowBaseStyle' template Border carries a Viewport3D
    // child plus a genuine net10.0-windows fixture compiling the exact
    // upstream CoverFlow block, byte-identical in every staged record shape
    // modulo per-file table numbering and line markers the staged file
    // omits: the 03 27 FD Viewport3D head, the 03 3D FE PerspectiveCamera
    // head, three 03 67 FE ModelVisual3D heads, two 03 74 FF
    // DirectionalLight heads, the 0B 78 FF Children list, the 19 .. FFFF
    // DefAttribute names, Position "0,0,3" with conv 31 FE, UpDirection
    // "0,1,0"/LookDirection "0,0,-1"/Direction "1,0,-4"/"-1,0,-4" with
    // conv 35 FD, Color "White" with conv A9 FF and FieldOfView "90" with
    // conv 51 FF; staged attrs 65/66 owner -729 Name/Camera, 67-69 owner
    // -490 Position/UpDirection/LookDirection, 70 owner -451 FieldOfView,
    // 71 owner -409 Content, 72 owner -349 Color, 73 owner -140 Direction).
    private const short KnownViewport3D = -729; // System.Windows.Controls.Viewport3D
    private const short KnownProjectionCamera = -490; // System.Windows.Media.Media3D.ProjectionCamera
    private const short KnownPerspectiveCamera = -451; // System.Windows.Media.Media3D.PerspectiveCamera
    private const short KnownModelVisual3D = -409; // System.Windows.Media.Media3D.ModelVisual3D
    private const short KnownLight = -349; // System.Windows.Media.Media3D.Light
    private const short KnownDirectionalLight = -140; // System.Windows.Media.Media3D.DirectionalLight
    private const short ConvPoint3D = -463; // Point3DConverter
    private const short ConvVector3D = -715; // Vector3DConverter
    // Viewport3D.Children list member (verified: the staged/fixture 0B 78 FF
    // PropertyIListStart; ILSpy PropertyListStartRecord carries the U16 id).
    private const short KnownViewportChildrenMember = -136;
    // Well-known TemplateBinding source member ids for the Border brush
    // geometry (verified: the real net10 fixture compiling
    // {TemplateBinding BorderBrush} emits E7 FF and {TemplateBinding
    // BorderThickness} emits E6 FF, including a swapped-source style proving
    // the ids track the source property names, not the target positions;
    // -24 Background reuses KnownBackgroundMember).
    private const short KnownBorderBrushSourceMember = -25;
    private const short KnownBorderThicknessSourceMember = -26;
    // Well-known template-extension members (verified against a real net10
    // BAML fixture compiling {TemplateBinding ...} on a template
    // ContentPresenter: the 0x23 payload is [targetAttr I16][extension U16]
    // [sourceRef I16]; 634 is the TemplateBinding discriminator, the target
    // is a well-known member id, and the source is a well-known member id
    // for framework-known templated-parent properties (-15
    // ContentControl.ContentTemplate, -35 Control.Padding) or a per-file
    // AttributeInfo index otherwise (staged #105 carries 11 =
    // FloatingBlock.ContentTemplate). Border visuals add the Background
    // (-4)/BorderBrush (-5)/BorderThickness (-6) targets with the matching
    // -24/-25/-26 sources, verified against the same fixture compiling a
    // Border template visual with all three TemplateBindings).
    private const ushort KnownTemplateBindingExtension = 634;
    // TemplateBindingExtension element type (verified: the staged
    // theme.baml BadgeDotTemplate Visibility value plus a genuine
    // net10.0-windows fixture compiling the same
    // TemplateBinding-with-Converter shape, both emitting 86 FD; owns the
    // Converter AttributeInfo row in both files).
    private const short KnownTemplateBinding = -634;
    private const short KnownContentTemplateMember = -21; // ContentPresenter.ContentTemplate (verified: {TemplateBinding ContentTemplate} target in the same fixture)
    private const short KnownHScrollBarVisibilityMember = -98; // ScrollViewer.HorizontalScrollBarVisibility member (verified: PWC "Hidden"/EnumConverter 9E FF on the staged RibbonTab ScrollViewer at BAML+0x1A9DD plus a genuine net10.0-windows fixture compiling the same Hidden/Disabled scrollbar block, byte-identical in every staged record shape)
    private const short KnownVScrollBarVisibilityMember = -99; // ScrollViewer.VerticalScrollBarVisibility member (verified: PWC "Disabled"/EnumConverter 9D FF on the same staged ScrollViewer plus the same fixture, emitting 9D FF)
    private const short KnownTemplateSourceContentTemplate = -15; // ContentControl.ContentTemplate (verified: {TemplateBinding ContentTemplate} source in the same fixture)
    private const short KnownPaddingMember = -35; // Control.Padding (verified: {TemplateBinding Padding} source in the same fixture)
    private const short KnownRelativeSource = -516; // System.Windows.Data.RelativeSource (verified: complex RelativeSource child on the staged #104 Binding plus the same real net10 BAML fixture)
    // Well-known template-attribute ids (the attribute table shared by
    // PropertyWithConverter/PropertyComplexStart inside template visuals:
    // negative ids are well-known members, non-negative ids index the
    // per-file AttributeInfo table like style-setter values).
    private const short KnownMarginAttribute = -49; // FrameworkElement.Margin member (verified: PWC "-20"/"-20,-20,-20,-20" with ThicknessConverter on a real net10 TextBlock plus the staged #104 FlipNumber children)
    private const short KnownTextAttribute = -110; // TextBlock.Text member (verified: complex Binding child on a real net10 TextBlock)
    private const short KnownTextTrimmingMember = -112; // TextBlock.TextTrimming member (verified: PropertyWithConverter "CharacterEllipsis"/EnumConverter 90 FF on the staged ComboBox template TextBlock at BAML+0x1A15F plus the staged DataGridTextColumnStyle short-form setter plus a genuine net10.0-windows fixture compiling the exact upstream block, byte-identical in every staged record shape)
    private const short KnownVisualTree = -174; // FrameworkTemplate.VisualTree (verified: ContentProperty inside the staged BlendEffectBox ControlTemplate)
    private const short KnownContentSource = -20; // ContentPresenter.ContentSource member (verified: PWC value "ActualContent" with StringConverter on the staged template child)
    // Animation-resource ids (verified: the staged theme.baml transition run
    // keyidx 113-121 plus a genuine net10.0-windows ResourceDictionary
    // fixture compiling two keyed Storyboards, byte-identical in every
    // record: ElementStart A0 FD, Children content 86 FF, DoubleAnimation
    // 57 FF, From/To as 0x05 inline values, Duration/TargetProperty as 0x24
    // values with converters -188/-492, EasingFunction as 0x07 complex
    // plus a single CubicEase element with one EasingMode 0x24 value).
    private const short KnownStoryboard = -608; // System.Windows.Media.Animation.Storyboard
    private const short KnownDoubleAnimation = -169; // System.Windows.Media.Animation.DoubleAnimation
    private const short KnownTimeline = -665; // System.Windows.Media.Animation.Timeline (verified: owner of the Duration AttributeInfo in both files)
    // Storyboard.Children content member (verified: 86 FF in both files; the
    // id shares its number with the KnownDataTrigger *type* id, but members
    // and types are separate well-known namespaces).
    private const short KnownStoryboardChildren = -122;
    private const short ConvPropertyPath = -492; // PropertyPathConverter (verified: TargetProperty path converter in both files)

    /// <summary>Loads the root visual from compiled BAML bytes (must be a Window).</summary>
    public static UIElement LoadRoot(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var r = new Parser(bytes, expectDictionary: false);
        return (UIElement)r.Parse();
    }

    /// <summary>Inflates one fresh visual from a retained DataTemplate slice
    /// (see <see cref="BamlTemplateSource"/>): reparses the exact subtree
    /// bytes against copies of the snapshotted tables, so instances,
    /// namescopes and bindings are independent per call while the parsed
    /// semantics stay shared. Throws precise when the slice references rows
    /// outside its snapshot (corrupt slice, never a shared instance).</summary>
    internal static UIElement InflateRetained(BamlTemplateSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Parser(source).ParseSingleRoot();
    }

    /// <summary>
    /// Loads a ResourceDictionary from compiled BAML bytes (must be a
    /// ResourceDictionary root: Source= is captured on
    /// <see cref="ResourceDictionary.Source"/> for the caller to resolve,
    /// keyed scalar entries land inline).
    /// </summary>
    public static ResourceDictionary LoadDictionary(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var r = new Parser(bytes, expectDictionary: true);
        return (ResourceDictionary)r.Parse();
    }

    /// <summary>One deferable-dictionary key: a string key (x:Key),
    /// an implicit-style type key ({x:Type ...}), or an element-form key
    /// ({x:Static ...}). Offsets are entry positions relative to the first
    /// content record (matching BamlDeferReader.SkipKeys); the entry parser
    /// validates each one, so a misframed file fails loud.</summary>
    private abstract record KeyRecord(uint Offset, bool Shared);
    private sealed record StringKeyRecord(string Key, uint Offset, bool Shared = true) : KeyRecord(Offset, Shared);
    private sealed record TypeKeyRecord(short TypeId, uint Offset, bool Shared = true) : KeyRecord(Offset, Shared);
    private sealed record StaticKeyRecord(short TypeId, string? Member, uint Offset, bool Shared = true) : KeyRecord(Offset, Shared);

    /// <summary>Retained BAML inflation source for one parsed DataTemplate:
    /// the exact subtree bytes plus snapshots of the tables the subtree can
    /// reference (taken when the template closed, a superset of every row it
    /// uses). LoadContent reparses the slice through a fresh Parser, so every
    /// inflation gets fresh instances, a fresh namescope and rewired bindings
    /// while sharing the parsed semantics. Never mutate a live snapshot: the
    /// reparse copies them again before parsing.</summary>
    internal sealed class BamlTemplateSource
    {
        internal BamlTemplateSource(
            byte[] slice,
            List<string> assemblies,
            List<(short AsmId, string Name)> types,
            List<(short OwnerId, string Name)> attrs,
            List<string> strings)
        {
            Slice = slice;
            Assemblies = assemblies;
            Types = types;
            Attrs = attrs;
            Strings = strings;
        }

        internal byte[] Slice { get; }
        internal List<string> Assemblies { get; }
        internal List<(short AsmId, string Name)> Types { get; }
        internal List<(short OwnerId, string Name)> Attrs { get; }
        internal List<string> Strings { get; }
    }

    private sealed class Parser(byte[] bytes, bool expectDictionary)
    {
        private readonly bool _expectDictionary = expectDictionary;
        private int _pos;
        private int _line = -1;
        private int _col = -1;
        private readonly List<string> _assemblies = new();
        private readonly List<(short AsmId, string Name)> _types = new();
        private readonly List<(short OwnerId, string Name)> _attrs = new();
        private readonly List<string> _strings = new();
        private readonly List<KeyRecord> _keys = new();
        // Per-key StaticResource declarations from the dictionary key block
        // (OptimizedStaticResource records ride with their key like the key
        // records themselves; the 0x38 PropertyWithStaticResourceId value id
        // inside an entry indexes this list for that entry, matching the
        // BAML KeyList[current].StaticResources shape). Flags mirrors the
        // record flags byte (0 is a plain string key; other flags name
        // x:Type/x:Static keys whose resolution is a follow-up); Key carries
        // the string-table name for flags 0 and is empty otherwise.
        private readonly List<List<(byte Flags, int Id, string Key)>> _keyStatics = new();
        // Xmlns prefix table (0x14 records: prefix + xmlns + assembly ids)
        // and namespace-mapping table (0x1B records: xmlns + clr-namespace
        // + assembly id), retained because command and static resolutions
        // need prefix mapping (the staged TabItemMenu entry resolves
        // 'interactivity:ControlCommands.*' through them).
        private readonly List<(string Prefix, string Xmlns, int[] AssemblyIds)> _xmlns = new();
        private readonly List<(string Xmlns, string ClrNamespace, int AssemblyId)> _piMapping = new();
        // StaticResource declarations for the entry currently parsing (set in
        // ParseEntry from the key block list; template Binding Converter
        // values resolve their 0x38 value id against it).
        private List<(byte Flags, int Id, string Key)>? _entryStatics;
        private int _keyIndex;
        private bool _indexOpen;
        private bool _entriesStarted;
        private ResourceDictionary? _dictRoot;
        // Inline 0x0D dictionary under construction (an element's .Resources
        // block: PropertyDictionaryStart carries the member id, entries ride
        // an inline deferable index, a nested ResourceDictionary element body
        // is transparent, PropertyDictionaryEnd closes).
        private sealed class InlineDictionaryFrame
        {
            public ResourceDictionary Dictionary { get; } = new();
            public object Owner { get; init; } = null!;
            public int KeyBase { get; init; }
            public int Claimed { get; set; }
            public bool NestedRd { get; set; }
            public bool IndexOpen { get; set; }
        }
        private readonly Stack<InlineDictionaryFrame> _inlineFrames = new();

        private ResourceDictionary? CurrentInlineDict => _inlineFrames.Count > 0 ? _inlineFrames.Peek().Dictionary : null;
        private int? _dictBase;
        // Stack elements are usually UIElement visuals; DataGridTextColumn
        // (not a visual) rides the same frames while its Columns list is
        // open, so the element type is object with per-site casts.
        private readonly Stack<(object Element, short? ContentMember)> _stack = new();
        // Open complex-property (0x07) frames, innermost last, by stack
        // depth after the frame push. A child ElementEnd whose pop lands
        // back on a complex depth attaches to the complex member then.
        private readonly Stack<int> _complexDepths = new();
        private UIElement? _root;
        private object? _columnsOwner;
        private Grid? _gridListOwner;
        private bool _gridListRows;
        private object? _modelListOwner;
        private System.Collections.IList? _modelList;
        // Document/retained namescope for in-element DefAttribute x:Names
        // (created on first use against the outermost element, so each
        // LoadContent inflation gets its own scope through its fresh
        // Parser; duplicates rejected like WPF).
        private NameScope? _docScope;

        /// <summary>Retained-template reparse: the slice plus copies of the
        /// table snapshots taken when the template closed (a superset of
        /// every row the slice references; the copies keep the live snapshot
        /// immutable while inline table rows inside a slice append locally).
        /// Document state (keys, dictionaries, statics) stays empty: template
        /// content never carries it, and any such record fails precise.</summary>
        internal Parser(BamlTemplateSource source)
            : this(source.Slice, false)
        {
            _assemblies.AddRange(source.Assemblies);
            _types.AddRange(source.Types);
            _attrs.AddRange(source.Attrs);
            _strings.AddRange(source.Strings);
        }

        /// <summary>Parses exactly one retained template subtree: the first
        /// ElementStart opens the root, its matching ElementEnd returns the
        /// fresh visual. Dispatch mirrors the document loop minus document
        /// and dictionary records (a slice never carries them; finding one
        /// is a corrupt slice, failed loud).</summary>
        internal UIElement ParseSingleRoot()
        {
            while (true)
            {
                var offset = _pos;
                var type = ReadByte();
                switch (type)
                {
                    case 0x03: ReadRetainedElementStart(offset); break;
                    case 0x04:
                        if (_stack.Count == 1 && _complexDepths.Count == 0)
                        {
                            var (retained, _) = _stack.Pop();
                            if (retained is DataTemplate template && template.VisualRoot is UIElement visual)
                                return visual;
                            Fail<object>(offset, "retained template closed without a visual root (a template takes its VisualTree content plus one visual; corrupt slice inflates nothing shared)");
                            throw new InvalidOperationException("unreachable");
                        }
                        EndElement(offset);
                        break;
                    case 0x1C: ReadAssemblyInfo(); break;
                    case 0x1D: ReadTypeInfo(); break;
                    case 0x1F: ReadAttributeInfo(); break;
                    case 0x20: ReadStringInfo(); break;
                    case 0x24: ReadPropertyWithConverter(offset); break;
                    case 0x10: ReadText(offset); break;
                    case 0x11: ReadTextWithConverter(offset); break;
                    case 0x2E: SetContentMember(offset); break;
                    case 0x07: ReadComplexPropertyStart(offset); break;
                    case 0x08: EndComplexProperty(offset); break;
                    case 0x2A: ReadBindingCtorPath(offset); break;
                    case 0x0B: ReadPropertyIListStart(offset); break;
                    case 0x0C: ReadPropertyIListEnd(offset); break;
                    case 0x1A: break; // EndAttributes: pure delimiter, no tree effect
                    case 0x1B: ReadPIMapping(offset); break;
                    case 0x14: ReadXmlnsProperty(offset); break;
                    case 0x2D: ReadConnectionId(offset); break;
                    case 0x35: _line = (int)ReadU32(); _col = (int)ReadU32(); break;
                    case 0x36: _col = (int)ReadU32(); break;
                    case 0x37: ReadOptimizedStaticResource(offset); break;
                    case 0x0D: ReadPropertyDictionaryStart(offset); break;
                    case 0x19: ReadDefAttribute(offset); break;
                    case 0x06: ReadPropertyCustomBackground(offset); break;
                    case 0x38: ReadPropertyWithStaticResource(offset); break;
                    default:
                        Fail<object>(offset, $"unsupported BAML record 0x{type:X2} inside a retained template (template content takes elements, properties, text, bindings and lists only; corrupt slice never inflates shared state)");
                        throw new InvalidOperationException("unreachable");
                }
            }
        }

        /// <summary>ElementStart inside a retained slice: identical to the
        /// document path minus the document-root branch (a slice root is an
        /// ordinary complex-child push, returned when its end empties the
        /// stack). Records the element's slice offset on DataTemplates so
        /// the matching ElementEnd can retain the exact subtree bytes.</summary>
        private void ReadRetainedElementStart(int offset)
        {
            var typeId = ReadI16();
            _pos += 1; // flags byte
            if (IsDictionaryContent())
            {
                ParseEntry(offset, typeId);
                return;
            }
            if (typeId == KnownStyle && IsComplexParent())
            {
                var (styleParent, styleMember) = _stack.Peek();
                AttachComplexChild(offset, styleParent, styleMember, ParseInlineStyle(offset, styleParent));
                return;
            }
            var element = CreateElement(typeId, offset);
            if (element is DataTemplate retained)
                retained.BamlStartOffset = offset;
            if (_stack.Count == 0 || IsComplexParent())
            {
                // Slice root (or a complex child): no parent to attach to
                // yet; the matching ElementEnd returns or attaches it.
                _stack.Push((element, null));
                return;
            }
            AttachChild(offset, element);
            _stack.Push((element, null));
        }

        public object Parse()
        {
            ReadHeader();
            while (true)
            {
                var offset = _pos;
                var type = ReadByte();
                switch (type)
                {
                    case 0x01: break; // DocumentStart
                    case 0x02: // DocumentEnd
                        if (_stack.Count != 0) Fail(offset, "unclosed BAML elements at end of document");
                        if (_keyIndex != _keys.Count) Fail(offset, "unclaimed BAML dictionary keys at end of document");
                        if (_expectDictionary)
                            return _dictRoot ?? Fail<ResourceDictionary>(offset, "empty BAML document (no root element)");
                        if (_root is not null)
                        {
                            ApplyImplicitStylesTree(_root);
                            TemplateApplier.RefreshLoadedTree(_root);
                            if (_root is FrameworkElement feRoot)
                                feRoot.RefreshBindingsTree();
                        }
                        return _root ?? Fail(offset, "empty BAML document (no root element)");
                    case 0x03: ReadElementStart(offset); break;
                    case 0x04: EndElement(offset); break;
                    case 0x1C: ReadAssemblyInfo(); break;
                    case 0x1D: ReadTypeInfo(); break;
                    case 0x1F: ReadAttributeInfo(); break;
                    case 0x20: ReadStringInfo(); break;
                    case 0x24: ReadPropertyWithConverter(offset); break;
                    case 0x10: ReadText(offset); break;
                    case 0x11: ReadTextWithConverter(offset); break;
                    case 0x25: ReadDeferableStart(offset); break;
                    case 0x26: ReadKeyPosition(offset); break;
                    case 0x27: ReadDefKeyType(offset); break;
                    case 0x28: ReadKeyElement(offset); break;
                    case 0x29: Fail(offset, "unbalanced BAML key-element end (no open KeyElementStart)"); break;
                    case 0x2B:
                        Fail(offset, "unbalanced constructor-parameters end (no ConstructorParametersStart)");
                        break;
                    case 0x2C: Fail(offset, "constructor parameter types need the markup-extension follow-up"); break;
                    case 0x37: ReadOptimizedStaticResource(offset); break;
                    case 0x2E: SetContentMember(offset); break;
                    case 0x07: ReadComplexPropertyStart(offset); break;
                    case 0x08: EndComplexProperty(offset); break;
                    case 0x2A: ReadBindingCtorPath(offset); break;
                    case 0x0D: ReadPropertyDictionaryStart(offset); break;
                    case 0x19: ReadDefAttribute(offset); break;
                    case 0x06: ReadPropertyCustomBackground(offset); break;
                    case 0x38: ReadPropertyWithStaticResource(offset); break;
                    case 0x05 when _stack.Count != 0 && _stack.Peek().Element is RelativeSource relativeSource:
                        ReadRelativeSourceProperty(offset, relativeSource);
                        break;
                    case 0x05: case 0x09: case 0x0A:
                    case 0x0E: case 0x12: case 0x21: case 0x22:
                    case 0x23: case 0x2F: case 0x30: case 0x31: case 0x32: case 0x33:
                    case 0x34:
                        Fail(offset, $"BAML record 0x{type:X2} ({DescribeRecord(type)}) outside any entry (style-surface/markup follow-up)");
                        break;
                    case 0x35: _line = (int)ReadU32(); _col = (int)ReadU32(); break;
                    case 0x36: _col = (int)ReadU32(); break;
                    case 0x1A: break; // EndAttributes: pure delimiter, no tree effect
                    case 0x1B: ReadPIMapping(offset); break;
                    case 0x14: ReadXmlnsProperty(offset); break;
                    case 0x2D:
                        // ConnectionId (fixed 5 bytes: type + I32 LE): namescope
                        // id for x:Name/Name, consumed by generated
                        // InitializeComponent. The id itself stays framing-only;
                        // the Name lands through ReadDefAttribute below (document
                        // namescope) or as PWC (see KnownName).
                        ReadConnectionId(offset);
                        break;
                    case 0x0B: ReadPropertyIListStart(offset); break;
                    case 0x0C: ReadPropertyIListEnd(offset); break;
                    default:
                        Fail(offset, $"unsupported BAML record 0x{type:X2} (v2 supports Window/Grid/TextBlock/TextBox/ScrollViewer/ListBox/ListBoxItem/TreeView/TreeViewItem/Menu/MenuItem/DockPanel/DataGrid/DataGridTextColumn content plus ResourceDictionary roots with string/type/element-form keys and Color/UInt32/Double/CornerRadius/Geometry/Thickness scalar entries and simple-setter Style entries only)");
                        break;
                }
            }
        }

        private void ReadHeader()
        {
            var start = _pos;
            var len = ReadI32();
            var sig = Encoding.Unicode.GetString(bytes, _pos, len);
            _pos += len;
            if (sig != "MSBAML") Fail(start, "not a BAML stream (bad signature)");
            var major = ReadU16();
            var minor = ReadU16();
            if (major != 0 || minor != 0x60)
                Fail(start, $"unsupported BAML version {major}.{minor:X} (v1 reads 0.60)");
            // 15 trailer bytes after the version, byte-identical in every
            // real file read so far (00 00 60 00 00 00 60 00 01 00 FF FF FF
            // FF 00). Anything else is undecoded: fail loud instead of
            // misreading the trailer as records (the old 14-byte skip read
            // the trailing 0x00 as record type BAML+0x22).
            ReadOnlySpan<byte> trailer =
            [
                0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
                0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00,
            ];
            if (_pos + trailer.Length > bytes.Length)
                Fail<object>(start, "truncated BAML header (missing version trailer)");
            for (var i = 0; i < trailer.Length; i++)
            {
                if (bytes[_pos + i] != trailer[i])
                    Fail<object>(start, $"unsupported BAML header trailer (v1 reads the 15-byte 00..60..01..FFFFFFFF..00 layout of real files)");
            }
            _pos += trailer.Length;
        }

        private void ReadAssemblyInfo()
        {
            var end = StartSized(out var offset);
            var id = (int)ReadU16();
            var name = ReadAnsiString();
            while (_assemblies.Count <= id) _assemblies.Add(string.Empty);
            _assemblies[id] = name;
            ExpectEnd(end, offset, "AssemblyInfo");
        }

        /// <summary>Reads an XmlnsProperty record (prefix + xmlns + U16
        /// assembly-id count + ids; the staged root carries the default,
        /// x, local, hc and interactivity prefixes in this shape).
        /// Anything else fails loud instead of misaligning the tables.</summary>
        private void ReadXmlnsProperty(int offset)
        {
            var end = StartSized(out _);
            var prefix = ReadAnsiString();
            var xmlns = ReadAnsiString();
            var count = (int)ReadU16();
            var ids = new int[count];
            for (var i = 0; i < count; i++) ids[i] = ReadU16();
            ExpectEnd(end, offset, "XmlnsProperty");
            _xmlns.Add((prefix, xmlns, ids));
        }

        /// <summary>Reads a PIMapping record (xmlns + clr-namespace + U16
        /// assembly id; the staged interactivity mapping carries the
        /// 'clr-namespace:HandyControl.Interactivity' pair in this shape).
        /// Anything else fails loud instead of misaligning the tables.</summary>
        private void ReadPIMapping(int offset)
        {
            var end = StartSized(out _);
            var xmlns = ReadAnsiString();
            var clrNamespace = ReadAnsiString();
            var assemblyId = (int)ReadU16();
            ExpectEnd(end, offset, "PIMapping");
            _piMapping.Add((xmlns, clrNamespace, assemblyId));
        }

        private void ReadTypeInfo()
        {
            var end = StartSized(out var offset);
            var id = (int)ReadU16();
            var asmId = (short)ReadU16();
            var name = ReadAnsiString();
            while (_types.Count <= id) _types.Add((0, string.Empty));
            _types[id] = (asmId, name);
            ExpectEnd(end, offset, "TypeInfo");
        }

        private void SkipSized(int offset)
        {
            var end = StartSized(out _);
            if (end < _pos || end > bytes.Length)
                Fail<object>(offset, "corrupt BAML record size");
            _pos = end;
        }

        private void ReadConnectionId(int offset)
        {
            // Fixed layout: no size prefix, I32 LE connection id.
            if (_pos + 4 > bytes.Length)
                Fail<object>(offset, "truncated BAML ConnectionId record");
            var id = ReadI32();
            if (id < 0)
                Fail<object>(offset, $"invalid BAML connection id {id}");
        }

        /// <summary>Reads an in-element DefAttribute x:Name (sized record:
        /// 7-bit ANSI name plus the U16 name id; both genuine TestApp sites —
        /// the 0x17D6 "WinFormsWindow" on the converted LayoutAnchorable and
        /// the 0x1A45 "LayoutDocumentPane" — carry the unassigned 0xFFFF id,
        /// which anything else fails precise against). FrameworkElement hosts
        /// take the CLR Name (mirroring the PWC KnownName path);
        /// converted-model hosts (non-UIElement closure instances, Name-less
        /// by probe) register in the document namescope only. The scope is
        /// created once per parse on the outermost element, so each
        /// LoadContent inflation keeps its own names through its fresh
        /// Parser; duplicates rejected like WPF. Dictionary roots are not
        /// DependencyObjects, so names there stay a precise boundary.</summary>
        /// <summary>Reads an in-element PropertyCustom brush Background
        /// (sized record: I16 attribute id + U16 serializer + bytes; the
        /// three genuine TestApp sites and the genuine fw122
        /// Background="Transparent" fixture all carry attr -86 with
        /// serializer 744 and the 5-byte 0x01 + LE ARGB payload). Lands a
        /// SolidColorBrush on the host FrameworkElement's Brush-typed
        /// Background DP (DP-first SetValue, consistent with the PWC Name
        /// and model routes). Anything else fails precise so a half-set
        /// Background never lands.</summary>
        private void ReadPropertyCustomBackground(int offset)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var serializer = ReadU16();
            if (_stack.Count == 0) Fail(offset, "BAML PropertyCustom outside any element");
            var element = _stack.Peek().Element;
            if (attrId != KnownPanelBackgroundMember && attrId != KnownTextBoxBackgroundMember)
                Fail<object>(offset, $"BAML PropertyCustom attribute {attrId} on {Describe(element)} (in-element PropertyCustom takes the Background members -86/-24; follow-up)");
            if (serializer != BrushValueSerializer)
                Fail<object>(offset, $"BAML PropertyCustom Background on {Describe(element)} uses serializer {serializer} (Background takes the packed-ARGB brush serializer 744; follow-up)");
            if (end - _pos != 5)
                Fail<object>(offset, $"BAML PropertyCustom Background on {Describe(element)} has {end - _pos} value bytes (packed-ARGB brushes take 0x01 plus 4 bytes; follow-up)");
            var mark = ReadByte();
            var blue = ReadByte();
            var green = ReadByte();
            var red = ReadByte();
            var alpha = ReadByte();
            ExpectEnd(end, offset, "PropertyCustom");
            if (mark != 0x01)
                Fail<object>(offset, $"BAML PropertyCustom Background on {Describe(element)} has marker 0x{mark:X2} (packed-ARGB brushes start with 0x01; follow-up)");
            if (element is FrameworkElement fe && DependencyProperty.FromName(element.GetType(), "Background") is { } bgDp && bgDp.PropertyType == typeof(Brush))
            {
                fe.SetValue(bgDp, new SolidColorBrush(Color.FromArgb(alpha, red, green, blue)));
            }
            else
            {
                Fail<object>(offset, $"BAML PropertyCustom Background on {Describe(element)} (Background lands on FrameworkElement hosts with a Brush-typed Background DP; follow-up)");
                throw new InvalidOperationException("unreachable");
            }
        }

        private void ReadDefAttribute(int offset)
        {
            var end = StartSized(out _);
            var name = ReadAnsiString();
            var nameId = ReadU16();
            ExpectEnd(end, offset, "DefAttribute");
            if (nameId != 0xFFFF)
                Fail<object>(offset, $"BAML DefAttribute '{name}' carries name id {nameId} (document names take the unassigned id; uid wiring is a follow-up)");
            if (string.IsNullOrEmpty(name))
                Fail<object>(offset, "BAML DefAttribute carries an empty name");
            if (_stack.Count == 0) Fail(offset, "BAML DefAttribute outside any element");
            var element = _stack.Peek().Element;
            if (element is FrameworkElement named)
            {
                if (!string.IsNullOrEmpty(named.Name) && !string.Equals(named.Name, name, StringComparison.Ordinal))
                    Fail<object>(offset, $"BAML name '{name}' conflicts with Name '{named.Name}' on {Describe(element)}");
                named.Name = name;
            }
            else if (!DockingIntegration.IsConvertedModelInstance(element))
                Fail<object>(offset, $"BAML name '{name}' on {Describe(element)} (names land on FrameworkElement or converted-model hosts only)");
            RegisterDocumentName(element, name, offset);
        }

        private void RegisterDocumentName(object element, string name, int offset)
        {
            if (_docScope is null)
            {
                _docScope = new NameScope();
                var bottom = _stack.Count > 0 ? _stack.ToArray()[^1].Element : element;
                if (bottom is DependencyObject scopeRoot)
                {
                    NameScope.SetNameScope(scopeRoot, _docScope);
                }
            }
            _docScope[name] = element;
        }

        private void ReadAttributeInfo()
        {
            var end = StartSized(out var offset);
            var id = (int)ReadU16();
            var owner = ReadI16();
            _pos += 1; // flags byte (reserved)
            var name = ReadAnsiString();
            while (_attrs.Count <= id) _attrs.Add((0, string.Empty));
            _attrs[id] = (owner, name);
            ExpectEnd(end, offset, "AttributeInfo");
        }

        private void ReadStringInfo()
        {
            var end = StartSized(out var offset);
            var id = (int)ReadU16();
            var name = ReadAnsiString();
            while (_strings.Count <= id) _strings.Add(string.Empty);
            _strings[id] = name;
            ExpectEnd(end, offset, "StringInfo");
        }

        private void ReadDeferableStart(int offset)
        {
            // Fixed layout: no size prefix, U32 skip-to-end distance. The
            // distance lands on the dictionary's closing ElementEnd (checked
            // below); the content itself is parsed, never skipped.
            if (_pos + 4 > bytes.Length)
                Fail<object>(offset, "truncated BAML deferable-content record");
            var skip = ReadU32();
            var landing = _pos + (int)skip;
            if (landing < 0 || landing >= bytes.Length || bytes[landing] != 0x04)
                Fail<object>(offset, $"BAML deferable content skips to 0x{landing:X} (v1 expects the dictionary ElementEnd)");
            _indexOpen = true;
        }

        /// <summary>True while key records are valid: at a dictionary root
        /// with an open deferable index (matching BamlDeferReader.SkipKeys,
        /// which treats DefAttributeKeyString/DefAttributeKeyType,
        /// OptimizedStaticResource and the StaticResource/KeyElement trees as
        /// the key block).</summary>
        private bool IsDictionaryIndex() =>
            // An open inline 0x0D frame collects its keys the same way a
            // root index does (frame parsing runs its own record loop, never
            // the main loop, so both share the key lists distinguished by a
            // per-frame base index).
            (_inlineFrames.Count > 0 && _inlineFrames.Peek().IndexOpen) ||
            _indexOpen && _dictRoot is not null && _stack.Count == 1 &&
            ReferenceEquals(_stack.Peek().Element, _dictRoot);

        private void ReadKeyPosition(int offset)
        {
            // DefAttributeKeyString: U16 string-table value id + U32 content
            // offset + U8 shared + U8 sharedSet (the v1 "zero pad" is the
            // shared pair; x:Shared="False" entries carry shared=0,sharedSet=1,
            // byte-verified on staged keys #94-99/#104-112/#150/#628-629).
            // The flag rides on the key record; non-shared converted entries
            // materialize one fresh instance per lookup. Keys are consumed in
            // order by the entry stream; the offset is checked there.
            var end = StartSized(out _);
            var keyId = (int)ReadU16();
            var entryOffset = ReadU32();
            var shared = ReadByte();
            var sharedSet = ReadByte();
            ExpectEnd(end, offset, "DefAttributeKeyString");
            if (!IsDictionaryIndex())
                Fail<object>(offset, "BAML key record outside a deferable dictionary index");
            if (keyId < 0 || keyId >= _strings.Count)
                Fail<object>(offset, $"BAML key id {keyId} has no StringInfo record");
            _keys.Add(new StringKeyRecord(_strings[keyId], entryOffset, Shared: sharedSet == 0 || shared != 0));
            _keyStatics.Add(new());
        }

        private void ReadDefKeyType(int offset)
        {
            // DefAttributeKeyType: U16 typeId + U8 flags + U32 content offset
            // + U8 shared + U8 sharedSet. The typeId is a per-file TypeInfo
            // index or a negated well-known id ({x:Type ...} implicit-style
            // keys); the CLR type is resolved when the entry materializes.
            if (_pos + 9 > bytes.Length)
                Fail<object>(offset, "truncated BAML type-key record");
            var typeId = ReadI16();
            _pos += 1; // flags byte
            var entryOffset = ReadU32();
            var shared = bytes[_pos];
            var sharedSet = bytes[_pos + 1];
            _pos += 2; // shared pair (see ReadKeyPosition)
            if (!IsDictionaryIndex())
                Fail<object>(offset, "BAML type key outside a deferable dictionary index");
            _keys.Add(new TypeKeyRecord(typeId, entryOffset, Shared: sharedSet == 0 || shared != 0));
            _keyStatics.Add(new());
        }

        private void ReadKeyElement(int offset)
        {
            // KeyElementStart: U16 typeId + U8 flags + U32 content offset +
            // U8 shared + U8 sharedSet, followed by the key element subtree
            // through the matching KeyElementEnd. v2 supports the observed
            // single-text shape ({x:Static Member} with constructor framing:
            // CtorStart, Text, CtorEnd); anything else fails loud.
            if (_pos + 9 > bytes.Length)
                Fail<object>(offset, "truncated BAML key-element record");
            var typeId = ReadI16();
            _pos += 1; // flags byte
            var entryOffset = ReadU32();
            var shared = bytes[_pos];
            var sharedSet = bytes[_pos + 1];
            _pos += 2; // shared pair (see ReadKeyPosition)
            if (!IsDictionaryIndex())
                Fail<object>(offset, "BAML key element outside a deferable dictionary index");
            string? member = null;
            var sawText = false;
            var sawCtorStart = false;
            var sawCtorEnd = false;
            var depth = 1;
            while (depth > 0)
            {
                if (_pos >= bytes.Length)
                    Fail<object>(offset, "truncated BAML key element (no KeyElementEnd)");
                var recordOffset = _pos;
                var record = ReadByte();
                switch (record)
                {
                    case 0x28: depth++; break;
                    case 0x29: depth--; break;
                    case 0x2A:
                        if (sawCtorStart || sawText || sawCtorEnd)
                            Fail<object>(recordOffset, "malformed BAML key element (v2 supports one constructor-text group only)");
                        sawCtorStart = true;
                        break;
                    case 0x2B:
                        if (!sawCtorStart || sawCtorEnd)
                            Fail<object>(recordOffset, "malformed BAML key element (constructor end without start)");
                        sawCtorEnd = true;
                        break;
                    case 0x10:
                        if (sawText)
                            Fail<object>(recordOffset, "malformed BAML key element (v2 supports single-text keys only)");
                        member = ReadSizedString(recordOffset, "key-element text");
                        sawText = true;
                        break;
                    case 0x35: _line = (int)ReadU32(); _col = (int)ReadU32(); break;
                    case 0x36: _col = (int)ReadU32(); break;
                    default:
                        Fail<object>(recordOffset, $"malformed BAML key element (record 0x{record:X2} inside a key; v2 supports single-text keys only)");
                        break;
                }
            }
            if (!sawText || string.IsNullOrEmpty(member))
                Fail<object>(offset, "malformed BAML key element (no key text)");
            _keys.Add(new StaticKeyRecord(typeId, member, entryOffset, Shared: sharedSet == 0 || shared != 0));
            _keyStatics.Add(new());
        }

        private void ReadOptimizedStaticResource(int offset)
        {
            // OptimizedStaticResource: U8 flags + U16 valueId (fixed 3 content
            // bytes). Inside the key index it declares one static resource
            // for the most recent key (collected per key like the key
            // records themselves, matching KeyList[].StaticResources); in
            // entry content it is a static-value reference and a
            // style-surface boundary.
            if (_pos + 3 > bytes.Length)
                Fail<object>(offset, "truncated BAML optimized-static-resource record");
            var flags = bytes[_pos];
            var keyId = (int)(bytes[_pos + 1] | (bytes[_pos + 2] << 8));
            _pos += 3;
            if (IsDictionaryIndex())
            {
                if (_keyStatics.Count == 0)
                    Fail<object>(offset, "BAML static resource without a dictionary key");
                if (flags == 0)
                {
                    if (keyId < 0 || keyId >= _strings.Count)
                        Fail<object>(offset, $"BAML static resource key id {keyId} has no StringInfo record");
                    _keyStatics[^1].Add((flags, keyId, _strings[keyId]));
                }
                else
                {
                    // x:Type/x:Static static keys (flags 1/2 with a type or
                    // member id): collected opaque with an empty name;
                    // resolving them is a style-surface follow-up.
                    _keyStatics[^1].Add((flags, keyId, string.Empty));
                }
                return;
            }
            Fail<object>(offset, "static resource values need the style-surface follow-up");
        }

        private void ReadTextWithConverter(int offset)
        {
            // Bare text-with-converter records live inside keyed entries and
            // are consumed by the entry machine; a bare one at dictionary
            // depth or in window content is corrupt (window text runs use
            // plain Text).
            var end = StartSized(out _);
            var value = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "TextWithConverter");
            if (IsDictionaryContent())
                Fail<object>(offset, "BAML text-with-converter outside any entry (entries are element subtrees; style-surface follow-up)");
            if (_stack.Count == 0) Fail(offset, "text outside any element");
            Fail<object>(offset, $"BAML text-with-converter inside {Describe(_stack.Peek().Element)} (v2 supports TextBlock text only)");
            _ = value;
            _ = conv;
        }

        /// <summary>True at a dictionary root that is awaiting entries
        /// (the entry machine consumes entry subtrees directly; records at
        /// this depth outside entries are corrupt).</summary>
        private bool IsDictionaryContent() =>
            _dictRoot is not null && _stack.Count == 1 &&
            ReferenceEquals(_stack.Peek().Element, _dictRoot);

        private void SetContentMember(int offset)
        {
            var member = ReadI16();
            if (_stack.Count == 0) Fail(offset, "ContentProperty outside any element");
            if (IsDictionaryContent())
                Fail<object>(offset, "ContentProperty on <ResourceDictionary> (content properties live inside entries; style-surface follow-up)");
            var top = _stack.Pop();
            _stack.Push((top.Element, member));
        }

        private bool IsComplexParent() =>
            _complexDepths.Count != 0 && _stack.Count == _complexDepths.Peek();

        private void ReadComplexPropertyStart(int offset)
        {
            if (_stack.Count == 0) Fail(offset, "complex property outside any element");
            if (_pos + 2 > bytes.Length) Fail<object>(offset, "truncated BAML complex-property start (needs the U16 member id)");
            var memberId = ReadI16();
            var (element, _) = _stack.Peek();
            _stack.Push((element, memberId));
            _complexDepths.Push(_stack.Count);
        }

        private void EndComplexProperty(int offset)
        {
            if (_complexDepths.Count == 0) Fail(offset, "unbalanced PropertyComplexEnd (no open complex property)");
            if (_stack.Count == 0) Fail(offset, "unbalanced PropertyComplexEnd (empty element stack)");
            if (_stack.Count != _complexDepths.Peek())
                Fail(offset, "PropertyComplexEnd with open child content (unbalanced element end)");
            _complexDepths.Pop();
            _stack.Pop();
        }

        private void AttachComplexChild(int offset, object parent, short? member, object child)
        {
            if (member is short memberId && memberId >= 0 && memberId < _attrs.Count
                && DockingIntegration.IsHostedInstance(parent))
            {
                // Hosted converted value (e.g. the converted theme on
                // DockingManager.Theme): the converted DP type-checks the
                // real converted instance; anything else fails precise.
                var (_, complexName) = _attrs[memberId];
                if (DockingIntegration.TryApplyConvertedValue(parent, complexName, child, out var complexFailure))
                    return;
                Fail<object>(offset, complexFailure!);
                throw new InvalidOperationException("unreachable");
            }
            if (member == KnownStyleMember && parent is FrameworkElement styled && child is Style inline)
            {
                // Inline <X.Style> complex value (verified: the real TestApp
                // DocumentHeaderTemplate icon carries its Style at BAML+0x1423):
                // the parsed Style applies immediately through the DP store,
                // so its Trigger/Setter semantics run on existing machinery.
                styled.Style = inline;
                return;
            }
            if (member == KnownImageSourceMember && parent is Image image)
            {
                // Image.Source travels as a complex value (verified: the
                // real TestApp DocumentHeaderTemplate icon binds
                // IconSource at BAML+0x140F): a Binding wires through the
                // store, a decoded ImageSource lands directly; anything
                // else stays precise.
                if (child is Binding imageBinding) { BindingOperations.SetBinding(image, Image.SourceProperty, imageBinding); return; }
                if (child is ImageSource imageSource) { image.Source = imageSource; return; }
                Fail<object>(offset, $"Image.Source holds {Describe(child)} (image sources take a Binding or an ImageSource; image-surface follow-up)");
            }
            if (member == KnownTextBlockText && parent is TextBlock textBlock && child is Binding textBinding)
            {
                // TextBlock.Text travels as a complex value too (verified:
                // the real TestApp DocumentHeaderTemplate Title binds
                // Title at BAML+0x1499): the literal-PWC path above stays
                // for plain text; bindings wire through the store.
                BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, textBinding);
                return;
            }
            if (member is short modelComplexId && modelComplexId >= 0 && modelComplexId < _attrs.Count
                && DockingIntegration.IsConvertedModelInstance(parent))
            {
                // Converted model complex child (e.g. a model Content set
                // as a complex value): same CLR-setter contract as content.
                var (_, modelComplexName) = _attrs[modelComplexId];
                if (DockingIntegration.TrySetConvertedModelMember(parent, modelComplexName, child, out var modelComplexFailure))
                    return;
                Fail<object>(offset, modelComplexFailure!);
                throw new InvalidOperationException("unreachable");
            }
            if (member is short rsMember && rsMember >= 0 && rsMember < _attrs.Count
                && parent is Binding rsBinding && child is RelativeSource relativeSource)
            {
                // Complex RelativeSource child on a document-level Binding
                // (verified: the real TestApp TextBox.Text binding carries
                // the -516 element at BAML+0x1CCE): the per-file member id varies by file,
                // so the attach keys on the (owner, name) pair, like the
                // converted-model arms above.
                var (_, rsMemberName) = _attrs[rsMember];
                if (!string.Equals(rsMemberName, "RelativeSource", StringComparison.Ordinal))
                    Fail<object>(offset, $"complex property member '{rsMemberName}' on <Binding> holds {Describe(child)} (bindings take a RelativeSource child only)");
                if (rsBinding.RelativeSource is not null)
                    Fail<object>(offset, "duplicate RelativeSource on the Binding (bindings take one RelativeSource)");
                rsBinding.RelativeSource = relativeSource;
                return;
            }
            if (member == KnownTextBoxText && parent is TextBox textBox && child is Binding boxBinding)
            {
                // Complex TextBox.Text holding a Binding (verified: the real
                // TestApp document TextBox binds its text through a
                // RelativeSource complex inside the Binding):
                // wires through the Text DP store, like TextBlock.Text above.
                BindingOperations.SetBinding(textBox, TextBox.TextProperty, boxBinding);
                return;
            }
            if (member == KnownBackgroundMember && child is Binding binding && parent is Control control)
            {
                BindingOperations.SetBinding(control, Control.BackgroundProperty, binding);
                return;
            }
            Fail<object>(offset, $"complex property member {member} on {Describe(parent)} holds {Describe(child)} (v1 routes Window/Control Background plus Image.Source plus TextBlock.Text bindings only)");
        }

        private static bool IsKnownResourcesMember(short memberId) =>
            memberId is -55 or -58 or -59 or -123 or -8 or -107;

        private void ReadPropertyDictionaryStart(int offset)
        {
            // PropertyDictionaryStart: U16 per-file attribute id naming a
            // dictionary-valued property (real files: FrameworkElement.
            // Resources for <X.Resources> blocks), followed by the inline
            // entries through PropertyDictionaryEnd. A nested
            // ResourceDictionary element body is transparent; anything else
            // fails precise so a half-built dictionary never lands.
            if (_pos + 2 > bytes.Length) Fail<object>(offset, "truncated BAML dictionary start (needs the U16 member id)");
            var memberId = ReadI16();
            if (_stack.Count == 0) Fail(offset, $"BAML dictionary start for member {memberId} outside any element (inline dictionaries attach to an element's Resources)");
            
            string memberName;
            short ownerId = 0;
            if (memberId >= 0 && memberId < _attrs.Count)
            {
                (ownerId, memberName) = _attrs[memberId];
            }
            else if (IsKnownResourcesMember(memberId))
            {
                memberName = "Resources";
            }
            else
            {
                Fail<object>(offset, $"BAML dictionary start names attribute id {memberId} with no AttributeInfo record");
                return;
            }

            if (!string.Equals(memberName, "Resources", StringComparison.Ordinal) && !memberName.EndsWith(".Resources", StringComparison.Ordinal))
                Fail<object>(offset, $"BAML dictionary start names '{memberName}' (owner {ownerId}; inline dictionaries attach to a Resources property)");
            var (element, _) = _stack.Peek();
            var frame = new InlineDictionaryFrame
            {
                Owner = element,
                KeyBase = _keys.Count,
                Claimed = 0,
                NestedRd = false,
                IndexOpen = false
            };
            _inlineFrames.Push(frame);
            try
            {
                ReadInlineDictionary(offset, memberId);
            }
            finally
            {
                if (_inlineFrames.Count > 0 && ReferenceEquals(_inlineFrames.Peek(), frame))
                    _inlineFrames.Pop();
            }
        }

        private void ReadInlineDictionary(int offset, short memberId)
        {
            var frame = _inlineFrames.Peek();
            while (true)
            {
                var recordOffset = _pos;
                var record = ReadByte();
                switch (record)
                {
                    case 0x0E:
                        if (frame.NestedRd) Fail(recordOffset, "PropertyDictionaryEnd closes an unclosed nested ResourceDictionary (each nested body needs its ElementEnd)");
                        if (_keys.Count - frame.KeyBase != frame.Claimed) Fail(recordOffset, $"PropertyDictionaryEnd with an unclaimed dictionary key (keys need their value element; style-surface follow-up)");
                        AttachInlineDictionary(offset, memberId, frame);
                        return;
                    case 0x03:
                        ReadInlineEntryOrBody(recordOffset, memberId);
                        break;
                    case 0x04:
                        if (!frame.NestedRd) Fail(recordOffset, "unbalanced ElementEnd in an inline resource dictionary (no open nested ResourceDictionary body)");
                        frame.NestedRd = false;
                        break;
                    case 0x25:
                        ReadDeferableStart(recordOffset);
                        frame.IndexOpen = true;
                        break;
                    case 0x26:
                        ReadKeyPosition(recordOffset);
                        break;
                    case 0x27:
                        ReadDefKeyType(recordOffset);
                        break;
                    case 0x28:
                        ReadKeyElement(recordOffset);
                        break;
                    case 0x37:
                        ReadOptimizedStaticResource(recordOffset);
                        break;
                    case 0x0D:
                        ReadPropertyDictionaryStart(recordOffset);
                        break;
                    case 0x24:
                        ReadInlineDictionaryProperty(recordOffset, frame);
                        break;
                    case 0x1C:
                        ReadAssemblyInfo();
                        break;
                    case 0x1D:
                        ReadTypeInfo();
                        break;
                    case 0x1F:
                        ReadAttributeInfo();
                        break;
                    case 0x20:
                        ReadStringInfo();
                        break;
                    case 0x14:
                        ReadXmlnsProperty(recordOffset);
                        break;
                    case 0x1B:
                        ReadPIMapping(recordOffset);
                        break;
                    case 0x35:
                        _line = (int)ReadU32(); _col = (int)ReadU32();
                        break;
                    case 0x36:
                        _col = (int)ReadU32();
                        break;
                    case 0x1A:
                        break;
                    case 0x2D:
                        ReadConnectionId(recordOffset);
                        break;
                    default:
                        Fail(recordOffset, $"record 0x{record:X2} inside the inline resource dictionary for member {memberId} (dictionaries take table records, an index, keyed entries and the nested body; style-surface follow-up)");
                        break;
                }
            }
        }

        private void ReadInlineDictionaryProperty(int offset, InlineDictionaryFrame frame)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var value = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "PropertyWithConverter");
            ApplyDictionaryProperty(offset, frame.Dictionary, attrId, value, conv);
        }

        private void ReadInlineEntryOrBody(int offset, short memberId)
        {
            var frame = _inlineFrames.Peek();
            var typeId = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            if (typeId == KnownResourceDictionary)
            {
                if (frame.NestedRd || frame.Claimed != 0 || _keys.Count - frame.KeyBase != 0) Fail<object>(offset, "second ResourceDictionary body in one inline dictionary (one inline dictionary takes one body)");
                frame.NestedRd = true;
                return;
            }
            if (_keys.Count - frame.KeyBase <= frame.Claimed) Fail<object>(offset, $"keyless value {WellKnownName(typeId)} in the inline resource dictionary for member {memberId} (each value needs its 0x26/0x27/0x28 key; style-surface follow-up)");
            var key = _keys[frame.KeyBase + frame.Claimed];
            _entryStatics = _keyStatics[frame.KeyBase + frame.Claimed];
            object entryKey;
            var display = DescribeKey(key);
            if (key is StaticKeyRecord staticKey)
            {
                entryKey = ResolveStaticKey(offset, staticKey);
            }
            else if (key is TypeKeyRecord typeKey)
            {
                if (typeKey.TypeId < 0)
                {
                    entryKey = ResolveTargetTypeId(offset, display, typeKey.TypeId);
                }
                else
                {
                    var (entryType, _) = ResolveEntryType(offset, display, typeKey.TypeId);
                    entryKey = entryType;
                }
            }
            else
            {
                entryKey = ((StringKeyRecord)key).Key;
            }
            ParseKeyedEntry(offset, entryKey, display, typeId, key.Shared);
            frame.Claimed++;
        }

        private void AttachInlineDictionary(int offset, short memberId, InlineDictionaryFrame frame)
        {
            var dict = frame.Dictionary;
            var owner = frame.Owner;
            if (owner is FrameworkElement fe)
            {
                if (dict.Source is not null)
                {
                    fe.Resources.MergedDictionaries.Add(dict);
                }
                foreach (var pair in dict.LocalEntries())
                {
                    try { fe.Resources.Add(pair.Key, pair.Value); }
                    catch (InvalidOperationException ex) { throw new XamlLoadException($"BAML+0x{offset:X} (line {_line}, pos {_col}): duplicate resource key {pair.Key} merging into {Describe(owner)} (inline dictionaries merge; {ex.Message})"); }
                }
                foreach (var merged in dict.MergedDictionaries)
                {
                    fe.Resources.MergedDictionaries.Add(merged);
                }
                return;
            }
            if (owner is Style style)
            {
                if (dict.Source is not null)
                {
                    style.Resources.MergedDictionaries.Add(dict);
                }
                foreach (var pair in dict.LocalEntries())
                {
                    try { style.Resources.Add(pair.Key, pair.Value); }
                    catch (InvalidOperationException ex) { throw new XamlLoadException($"BAML+0x{offset:X} (line {_line}, pos {_col}): duplicate resource key {pair.Key} merging into {Describe(owner)} (inline dictionaries merge; {ex.Message})"); }
                }
                foreach (var merged in dict.MergedDictionaries)
                {
                    style.Resources.MergedDictionaries.Add(merged);
                }
                return;
            }
            Fail<object>(offset, $"inline resource dictionary for member {memberId} on {Describe(owner)} (inline dictionaries attach to FrameworkElement or Style Resources)");
        }

        private bool TryFindResourceInScope(string key, out object? resource)
        {
            if (CurrentInlineDict is not null && CurrentInlineDict.TryGetValue(key, out resource))
                return true;
            foreach (var (stackItem, _) in _stack)
            {
                if (stackItem is FrameworkElement stackFe && stackFe.TryFindResource(key, out resource))
                    return true;
            }
            if (_dictRoot is not null && _dictRoot.TryGetValue(key, out resource))
                return true;
            if (Application.Current is not null && Application.Current.TryFindResource(key, out resource))
                return true;
            resource = null;
            return false;
        }

        private void ReadPropertyWithStaticResource(int offset)
        {
            if (_pos + 4 > bytes.Length)
                Fail<object>(offset, "truncated BAML property with static resource");
            var attrId = ReadI16();
            var staticId = (int)ReadU16();
            if (_stack.Count == 0)
                Fail(offset, "property with static resource outside any element");
            var (element, _) = _stack.Peek();

            string key;
            if (_entryStatics is not null && staticId >= 0 && staticId < _entryStatics.Count)
            {
                key = _entryStatics[staticId].Key;
            }
            else if (staticId >= 0 && staticId < _strings.Count)
            {
                key = _strings[staticId];
            }
            else
            {
                Fail<object>(offset, $"BAML static resource id {staticId} has no StringInfo record");
                return;
            }

            if (!TryFindResourceInScope(key, out var resource) || resource is null)
            {
                Fail<object>(offset, $"StaticResource '{key}' was not found in element, ancestor or application resources.");
                return;
            }

            ApplyStaticResourceProperty(offset, element, attrId, key, resource);
        }

        private void ApplyStaticResourceProperty(int offset, object element, short attrId, string key, object resource)
        {
            string? attrName = null;
            if (attrId >= 0 && attrId < _attrs.Count)
            {
                var (_, name) = _attrs[attrId];
                attrName = name;
            }
            else
            {
                attrName = WellKnownMemberName(attrId);
            }

            if (element is FrameworkElement fe)
            {
                if (attrName == "Style")
                {
                    if (resource is Style style)
                    {
                        fe.Style = style;
                        return;
                    }
                    Fail<object>(offset, $"StaticResource '{key}' is not a Style for Style property on {Describe(element)}");
                }
                if (attrName == "Background")
                {
                    if (element is Control control) { control.Background = CoerceBrush(resource); return; }
                    if (element is Panel panel) { panel.Background = CoerceBrush(resource); return; }
                    if (element is Border border) { border.Background = CoerceBrush(resource); return; }
                }
                if (attrName == "Foreground")
                {
                    if (element is Control cFore) { cFore.Foreground = CoerceBrush(resource); return; }
                    if (element is TextBlock tbFore) { tbFore.Foreground = CoerceBrush(resource); return; }
                }
                if (attrName == "BorderBrush")
                {
                    if (element is Control cBorder) { cBorder.BorderBrush = CoerceBrush(resource); return; }
                    if (element is Border bBorder) { bBorder.BorderBrush = CoerceBrush(resource); return; }
                }
                if (attrName is not null)
                {
                    var dp = DependencyProperty.FromName(fe.GetType(), attrName);
                    if (dp is not null)
                    {
                        fe.SetValue(dp, resource);
                        return;
                    }
                    var prop = element.GetType().GetProperty(attrName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop is not null && prop.CanWrite)
                    {
                        prop.SetValue(element, resource);
                        return;
                    }
                }
            }
            Fail<object>(offset, $"Unsupported static resource property attribute id {attrId} ('{attrName}') on {Describe(element)}");
        }

        private static Brush CoerceBrush(object resource) =>
            resource switch
            {
                Brush b => b,
                Color c => new SolidColorBrush(c),
                uint rgba => new SolidColorBrush(StyleValueConverter.ColorFromRgba(rgba)),
                _ => new SolidColorBrush(Colors.Transparent)
            };

        private static void ApplyImplicitStylesTree(UIElement root)
        {
            if (root is FrameworkElement fe)
            {
                if (fe.Style is null && TryFindImplicitStyle(fe, out var style) && style is not null)
                {
                    fe.Style = style;
                }
                foreach (var child in fe.LogicalChildren)
                {
                    ApplyImplicitStylesTree(child);
                }
            }
        }

        private static bool TryFindImplicitStyle(FrameworkElement element, out Style? style)
        {
            for (var t = element.GetType(); t != null && t != typeof(UIElement) && t != typeof(object); t = t.BaseType)
            {
                if (element.TryFindResource(t, out var res) && res is Style s)
                {
                    style = s;
                    return true;
                }
            }
            style = null;
            return false;
        }

        private void ReadBindingCtorPath(int offset)
        {
            if (_stack.Peek().Element is Binding binding)
            {
                var args = ReadCtorParams(offset, "the Binding element");
                if (args.Count > 1)
                    Fail<object>(offset, $"Binding element takes one constructor argument (the path; richer markup-extension arguments are a follow-up)");
                if (args.Count == 1) binding.Path = new PropertyPath(args[0]);
            }
            else if (_stack.Peek().Element is RelativeSource source)
            {
                var args = ReadCtorParams(offset, "the RelativeSource element");
                if (args.Count != 1)
                    Fail<object>(offset, $"RelativeSource element takes one constructor argument (the mode; richer markup-extension arguments are a follow-up)");
                try { source.Mode = Enum.Parse<RelativeSourceMode>(args[0], ignoreCase: false); }
                catch (ArgumentException)
                {
                    Fail<object>(offset, $"RelativeSource names '{args[0]}' (relative-source modes take FindAncestor/Self/TemplatedParent/PreviousData; follow-up)");
                }
            }
            else
                Fail(offset, "constructor parameters outside a Binding element (markup-extension follow-up)");
        }

        /// <summary>Parses a 0x05 Property record on a document-level
        /// RelativeSource element (its ElementStart is consumed): the only
        /// in-element property is AncestorType, naming a framework type
        /// (real TestApp: Window). Anything else fails precise.</summary>
        private void ReadRelativeSourceProperty(int offset, RelativeSource source)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            ExpectEnd(end, offset, "Property");
            if (!IsRelativeSourceAncestorTypeAttribute(attrId))
                Fail<object>(offset, $"RelativeSource holds attribute {BindingAttributeDisplay(attrId)} (relative sources take AncestorType; follow-up)");
            if (source.AncestorType is not null)
                Fail<object>(offset, "duplicate AncestorType on the RelativeSource (relative sources take one AncestorType)");
            source.AncestorType = ResolveDocumentAncestorType(offset, text);
        }

        /// <summary>Resolves a document-level RelativeSource AncestorType
        /// name to a loaded type: a bare name resolves an engine short
        /// name (real TestApp: Window), like the template resolver;
        /// anything else fails precise.</summary>
        private Type ResolveDocumentAncestorType(int offset, string text)
        {
            var engine = XamlLoader.FindEngineType(text);
            if (engine is not null) return engine;
            return Fail<Type>(offset, $"RelativeSource AncestorType '{text}' (relative-source ancestor types take an engine type; follow-up)");
        }

        /// <summary>Parses an inline Style complex value (the ElementStart type
        /// id is consumed by the caller): a PropertyTypeReference TargetType
        /// plus a Triggers list, closed by ElementEnd. The real TestApp
        /// DocumentHeaderTemplate icon carries exactly this shape (Style
        /// targeting Image with one Source=={x:Null} Trigger hiding the icon
        /// through a Visibility=Collapsed Setter, byte-verified against the
        /// genuine net10.0-windows fixture E:\tmp\fw121); anything richer
        /// fails precise so a half-built style never lands. TargetType,
        /// Trigger and Setter records reuse the keyed-entry helpers, so
        /// inline and keyed styles share one semantic machine.</summary>
        private Style ParseInlineStyle(int offset, object parent)
        {
            var display = $"the inline Style on {Describe(parent)}";
            Type? targetType = null;
            var triggers = new List<TriggerBase>();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"style {display} has no TargetType (inline styles take TargetType plus Triggers; style-surface follow-up)");
                    var style = new Style(targetType!);
                    foreach (var trigger in triggers) style.Triggers.Add(trigger);
                    return style;
                }
                if (record == 0x22)
                {
                    if (targetType is not null)
                        Fail<object>(recordOffset, $"duplicate TargetType on style {display}");
                    var attrId = ReadI16();
                    var styleTypeId = ReadI16();
                    RequireAttribute(recordOffset, display, attrId, KnownStyle, "TargetType");
                    targetType = styleTypeId == KnownImage
                        ? typeof(Image)
                        : ResolveTargetTypeId(recordOffset, display, styleTypeId);
                    continue;
                }
                if (record == 0x0B)
                {
                    if (targetType is null)
                        Fail<object>(recordOffset, $"trigger list on style {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    ParseTriggerList(recordOffset, offset, display, targetType!, triggers, KnownStyle);
                    continue;
                }
                Fail<object>(recordOffset, $"style {display} holds record 0x{record:X2} (inline styles take TargetType plus Triggers; style-surface follow-up)");
            }
        }

        private void ReadElementStart(int offset)
        {
            var typeId = ReadI16();
            _pos += 1; // flags byte
            if (_stack.Count == 0)
            {
                ReadRootElement(offset, typeId);
                return;
            }
            if (IsDictionaryContent())
            {
                ParseEntry(offset, typeId);
                return;
            }
            if (typeId == KnownStyle && IsComplexParent())
            {
                // Inline <X.Style> complex child (verified: the real TestApp
                // DocumentHeaderTemplate icon at BAML+0x142A): the dedicated
                // sub-loop reuses the keyed-entry TargetType/Trigger/Setter/
                // Null machinery and attaches immediately, so the generic
                // EndElement path never sees a half-built style.
                var (styleParent, styleMember) = _stack.Peek();
                AttachComplexChild(offset, styleParent, styleMember, ParseInlineStyle(offset, styleParent));
                return;
            }
            var element = CreateElement(typeId, offset);
            if (element is DataTemplate created)
                created.BamlStartOffset = offset;
            if (IsComplexParent())
            {
                // Complex-property child (e.g. the Binding in
                // <Window.Background>): attach at ElementEnd when the
                // child subtree is complete, never half-built.
                _stack.Push((element, null));
                return;
            }
            AttachChild(offset, element);
            _stack.Push((element, null));
        }

        private void ReadRootElement(int offset, short typeId)
        {
            if (_expectDictionary)
            {
                if (typeId != KnownResourceDictionary)
                    Fail<object>(offset, $"BAML root must be a ResourceDictionary (type id {typeId}; Window roots load through LoadRoot)");
                _dictRoot = new ResourceDictionary();
                _stack.Push((_dictRoot, null));
                return;
            }
            if (typeId == KnownResourceDictionary)
                Fail(offset, "BAML root is a ResourceDictionary (load it through LoadDictionary)");
            var element = CreateElement(typeId, offset);
            if (element is Window w) _root = w;
            else if (element is UserControl u) _root = u;
            else Fail(offset, $"BAML root must be a Window or converted-component UserControl (have {Describe(element)})");
            _stack.Push((element, null));
        }

        private void ParseEntry(int offset, short typeId)
        {
            // One keyed entry at a ResourceDictionary root. Keys are consumed
            // in index order; the entry must start exactly at the defer
            // position (first content offset + key offset), so a misframed
            // file fails loud instead of misaligning. The caller already
            // consumed the ElementStart type id; the machine below consumes
            // the rest of the entry subtree (or fails precise first: entries
            // are never half-built).
            if (_keyIndex >= _keys.Count)
                Fail<object>(offset, $"keyless BAML dictionary entry (type id {typeId}; v2 supports keyed entries only)");
            if (_keyIndex >= _keyStatics.Count)
                Fail<object>(offset, $"BAML dictionary entry has no static-resource index (only {_keyStatics.Count} collected)");
            _entryStatics = _keyStatics[_keyIndex];
            var key = _keys[_keyIndex];
            if (_dictBase is null)
            {
                _indexOpen = false;
                _dictBase = offset - (int)key.Offset;
            }
            else if (offset - _dictBase != (int)key.Offset)
                Fail<object>(offset, $"dictionary key {DescribeKey(key)} points at 0x{(_dictBase + (int)key.Offset):X} but the entry starts at 0x{offset:X}");
            _entriesStarted = true;
            if (key is StaticKeyRecord staticKey)
            {
                var resolved = ResolveStaticKey(offset, staticKey);
                ParseKeyedEntry(offset, resolved, DescribeKey(key), typeId, key.Shared);
                return;
            }
            if (key is TypeKeyRecord typeKey)
            {
                // Implicit-style type keys resolve to the entry CLR type
                // without instantiating it (engine short names and converted
                // full names, like style TargetTypes below, plus the
                // verified well-known framework ids such as the staged #103
                // ContentControl key); the style lands under the Type object,
                // which is how implicit lookups key.
                if (typeKey.TypeId < 0)
                {
                    var wellKnown = ResolveTargetTypeId(offset, DescribeKey(key), typeKey.TypeId);
                    ParseKeyedEntry(offset, wellKnown, DescribeKey(key), typeId, key.Shared);
                    return;
                }
                var (entryType, _) = ResolveEntryType(offset, DescribeKey(key), typeKey.TypeId);
                ParseKeyedEntry(offset, entryType, DescribeKey(key), typeId, key.Shared);
                return;
            }
            var name = ((StringKeyRecord)key).Key;
            ParseKeyedEntry(offset, name, DescribeKey(key), typeId, key.Shared);
        }

        /// <summary>Resolves an element-form x:Static dictionary key
        /// (StaticExtension with a single "Type.Member" constructor text,
        /// byte-verified on all 11 staged key elements) to the engine static
        /// key object. The member must name a public static ResourceKey on an
        /// engine key owner (SystemParameters/ToolBar/StatusBar, the staged
        /// set); anything else fails precise. Singletons are returned, so the
        /// loaded entry and later lookups share identity.</summary>
        private object ResolveStaticKey(int offset, StaticKeyRecord key)
        {
            if (key.TypeId != KnownStaticExtension)
                Fail<object>(offset, $"keyed entry {DescribeKey(key)} uses key-element type {WellKnownName(key.TypeId)} (x:Static keys travel as StaticExtension; follow-up work)");
            var member = DecodeStaticMember(key.Member ?? string.Empty);
            var dot = member.IndexOf('.');
            if (dot <= 0)
                Fail<object>(offset, $"keyed entry {DescribeKey(key)} names '{member}' (x:Static keys take Type.Member; follow-up work)");
            var typeName = member[..dot];
            var memberName = member[(dot + 1)..];
            Type? owner = typeName switch
            {
                "SystemParameters" => typeof(SystemParameters),
                "ToolBar" => typeof(ToolBar),
                "StatusBar" => typeof(StatusBar),
                _ => null,
            };
            if (owner is null)
                Fail<object>(offset, $"keyed entry {DescribeKey(key)} names unsupported x:Static type '{typeName}' (SystemParameters/ToolBar/StatusBar carry the staged keys; follow-up work)");
            const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            object? value = null;
            var property = owner.GetProperty(memberName, StaticFlags);
            if (property is not null && property.GetIndexParameters().Length == 0 && property.GetMethod?.IsPublic == true)
                value = property.GetValue(null);
            else
            {
                var field = owner.GetField(memberName, StaticFlags);
                if (field is not null) value = field.GetValue(null);
            }
            if (value is null)
                Fail<object>(offset, $"keyed entry {DescribeKey(key)} names unknown static member '{typeName}.{memberName}' (follow-up work)");
            if (value is not ResourceKey)
                Fail<object>(offset, $"keyed entry {DescribeKey(key)} resolves '{typeName}.{memberName}' to a {value!.GetType().Name} (x:Static dictionary keys must be ResourceKey; follow-up work)");
            return value!;
        }

        /// <summary>Materializes one keyed entry value under an already
        /// resolved key (string keys land under their text, x:Static keys
        /// under the resolved key object). Display carries the quoted form
        /// for precise errors. Non-shared keys (x:Shared="False") only change
        /// converted-element entries: those materialize per lookup, while
        /// scalar/empty/style entries (all shared in the staged file) add
        /// the single loaded value.</summary>
        private void ParseKeyedEntry(int offset, object entryKey, string display, short typeId, bool shared)
        {
            if (typeId >= 0)
            {
                if (string.Equals(TypeInfoName(typeId), DropShadowEffectTypeName, StringComparison.Ordinal))
                {
                    ParseDropShadowEffectEntry(offset, entryKey, display, shared, typeId);
                    return;
                }
                ParseCustomElementEntry(offset, entryKey, display, typeId, shared);
                return;
            }
            switch (typeId)
            {
                case KnownColor:
                case KnownUInt32:
                case KnownDouble:
                case KnownCornerRadius:
                case KnownGeometry:
                case KnownThickness:
                    ParseScalarEntry(offset, entryKey, display, typeId);
                    return;
                case KnownPathGeometry:
                    ParsePathGeometryEntry(offset, entryKey, display, shared);
                    return;
                case KnownBoolToVis:
                case KnownMenuScrolling:
                    ParseEmptyElementEntry(offset, entryKey, display, typeId);
                    return;
                case KnownStyle:
                    ParseStyleEntry(offset, entryKey, display, shared);
                    return;
                case KnownStoryboard:
                    ParseStoryboardEntry(offset, entryKey, display, shared);
                    return;
                case KnownControlTemplate:
                    ParseControlTemplateEntry(offset, entryKey, display, shared);
                    return;
                case KnownItemsPanelTemplate:
                    ParseItemsPanelTemplateEntry(offset, entryKey, display, shared);
                    return;
                case KnownLinearGradientBrush:
                    ParseLinearGradientBrushEntry(offset, entryKey, display, shared);
                    return;
                case KnownSolidColorBrush:
                    ParseSolidColorBrushEntry(offset, entryKey, display, shared);
                    return;
                case KnownDrawingBrush:
                    ParseDrawingBrushEntry(offset, entryKey, display, shared);
                    return;
                case KnownContextMenu:
                    ParseContextMenuEntry(offset, entryKey, display, shared);
                    return;
                default:
                    Fail<object>(offset, $"keyed entry {display} holds {WellKnownName(typeId)} (style-surface follow-up)");
                    return;
            }
        }

        /// <summary>Parses one keyed ControlTemplate entry (the ElementStart
        /// is consumed): a ControlTemplate-typed TargetType, the VisualTree
        /// content member, one visual plus an optional Triggers list, closed
        /// by ElementEnd. The template lands as a live engine ControlTemplate
        /// with its visual root, namescope and deferred bindings (inert until
        /// BAML-tree inflation lands, like the setter-form templates).
        /// Display strings name the entry key twice (there is no style
        /// TargetType/Setter around a keyed template). The staged
        /// 'GroupBoxBaseTopTemplate' entry is this exact shape (GroupBox
        /// target, Grid visual with RowDefinitions plus two Borders carrying
        /// TemplateBinding and TemplatedParent-bound values) and the staged
        /// 'GroupBoxBaseLeftTemplate' entry reuses it (ColumnDefinitions in
        /// place of RowDefinitions plus a LayoutTransform chain on the header
        /// presenter); non-shared keys are a precise follow-up. Anything
        /// richer fails precise so a half-built resource never lands.</summary>
        private void ParseControlTemplateEntry(int offset, object entryKey, string display, bool shared)
        {
            // Display strings name the entry key twice (there is no style
            // TargetType/Setter around a keyed template); the setter slot
            // takes the unquoted key so messages quote it exactly once.
            var template = ParseControlTemplate(offset, display, display.Trim('\''));
            if (shared) AddEntry(offset, entryKey, display, template);
            else Fail<object>(offset, $"keyed entry {display} is non-shared (keyed templates take shared entries; style-surface follow-up)");
        }
        /// <summary>Parses one keyed ContextMenu entry (the ElementStart is
        /// consumed): the Items content member, MenuItem row children (each
        /// an ElementStart holding one converted Command value plus one
        /// x:Static Header value, closed by ElementEnd), closed by the entry
        /// ElementEnd. The menu lands as a live engine ContextMenu with live
        /// MenuItem rows (shared lands once while non-shared replays the
        /// validated bags through a factory so every lookup materializes a
        /// fresh instance, like the converted-element and brush runs). The
        /// staged keyidx-160 'TabItemMenu' entry is this exact shape (three
        /// MenuItem rows binding HandyControl.Interactivity.ControlCommands
        /// Close/CloseAll/CloseOther with HandyControl.Properties.Langs.Lang
        /// Close/CloseAll/CloseOther headers, keyed x:Shared="False" so every
        /// lookup materializes fresh rows). Anything richer (nested rows,
        /// other properties, other value forms) fails precise so a half-built
        /// menu never lands.</summary>
        private void ParseContextMenuEntry(int offset, object entryKey, string display, bool shared)
        {
            var record = ReadEntryRecord(out var recordOffset);
            if (record != 0x2E)
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} (ContextMenu entries take the Items content; menu-surface follow-up)");
            var member = ReadI16();
            if (member != KnownItems)
                Fail<object>(recordOffset, $"content member {member} on context-menu entry {display} (ContextMenu entries take the Items content; menu-surface follow-up)");
            var rows = new List<(ICommand Command, object? Header)>();
            while (true)
            {
                record = ReadEntryRecord(out recordOffset);
                if (record == 0x04)
                {
                    if (rows.Count == 0)
                        Fail<object>(offset, $"keyed entry {display} holds an empty ContextMenu (ContextMenu entries take MenuItem rows; menu-surface follow-up)");
                    ContextMenu Materialize()
                    {
                        var menu = new ContextMenu();
                        foreach (var (command, header) in rows)
                        {
                            var row = new MenuItem { Command = command, Header = header };
                            menu.AddItem(row);
                        }
                        return menu;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Materialize());
                    else AddDeferredEntry(offset, entryKey, display, Materialize);
                    return;
                }
                if (record != 0x03)
                    Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a ContextMenu (ContextMenu entries take MenuItem rows; menu-surface follow-up)");
                var elementType = ReadI16();
                _pos += 1; // flags byte (skipped like ElementStart)
                if (elementType != KnownMenuItem)
                    Fail<object>(recordOffset, $"element {WellKnownName(elementType)} in context-menu entry {display} (ContextMenu entries take MenuItem rows; menu-surface follow-up)");
                rows.Add(ParseMenuItemRow(recordOffset, display));
            }
        }

        /// <summary>Parses one MenuItem row inside a keyed ContextMenu entry
        /// (its ElementStart is consumed): one converted Command
        /// PropertyWithConverter plus one x:Static Header
        /// PropertyWithExtension, closed by ElementEnd. Both values resolve
        /// eagerly with entry offsets, so the first failure is precise; the
        /// validated (Command, Header) bag replays per lookup for non-shared
        /// keys.</summary>
        private (ICommand Command, object? Header) ParseMenuItemRow(int offset, string display)
        {
            ICommand? command = null;
            object? header = null;
            var haveHeader = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (command is null)
                        Fail<object>(offset, $"MenuItem row in context-menu entry {display} has no Command (menu rows take a converted Command plus an x:Static Header; menu-surface follow-up)");
                    if (!haveHeader)
                        Fail<object>(offset, $"MenuItem row in context-menu entry {display} has no Header (menu rows take a converted Command plus an x:Static Header; menu-surface follow-up)");
                    return (command, header);
                }
                if (record == 0x24)
                {
                    if (command is not null)
                        Fail<object>(recordOffset, $"duplicate Command on the MenuItem row in context-menu entry {display}");
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    command = ReadMenuItemCommand(recordOffset, display, attrId, text, conv);
                    continue;
                }
                if (record == 0x23)
                {
                    if (haveHeader)
                        Fail<object>(recordOffset, $"duplicate Header on the MenuItem row in context-menu entry {display}");
                    header = ReadMenuItemHeader(recordOffset, display);
                    haveHeader = true;
                    continue;
                }
                Fail<object>(recordOffset, $"MenuItem row in context-menu entry {display} holds record 0x{record:X2} (menu rows take a converted Command plus an x:Static Header; menu-surface follow-up)");
            }
        }

        /// <summary>Reads one MenuItem Command value: a PropertyWithConverter
        /// over the MenuItem-owned Command row with the Command converter,
        /// carrying prefix:Type.Member text resolved through the file xmlns
        /// and namespace-mapping tables to a converted static command (the
        /// staged values name HandyControl.Interactivity.ControlCommands
        /// members, which the converter rewrote onto the engine RoutedCommand
        /// type, so the resolved value assigns directly).</summary>
        private ICommand ReadMenuItemCommand(int recordOffset, string display, short attrId, string text, short conv, string surface = "context-menu entry")
        {
            if (attrId < 0 || attrId >= _attrs.Count)
                Fail<object>(recordOffset, $"command property id {attrId} in {surface} {display} has no AttributeInfo record");
            var (owner, name) = _attrs[attrId];
            if (owner != KnownMenuItem || name != "Command")
                Fail<object>(recordOffset, $"attribute '{name}' in {surface} {display} (menu rows take the MenuItem Command; menu-surface follow-up)");
            if (conv != ConvCommand)
                Fail<object>(recordOffset, $"Command value with converter {conv} in {surface} {display} (menu commands take the Command converter; menu-surface follow-up)");
            return ResolveMenuCommand(recordOffset, display, text, surface);
        }

        /// <summary>Resolves prefix:Type.Member command text through the file
        /// xmlns table (prefix to xmlns) and namespace-mapping table (xmlns to
        /// clr-namespace plus assembly) to the converted static member, which
        /// must be an engine command.</summary>
        private ICommand ResolveCanonicalMenuCommand(int recordOffset, string display, string text, string surface)
        {
            // Bare command names (verified: the TestApp main window carries
            // Undo/Redo/Cut/Copy/Paste with no prefix) resolve through the
            // canonical command holders like the WPF CommandConverter: the
            // name must hit exactly one holder, otherwise the row stays
            // loud instead of binding the wrong command.
            var holders = new[]
            {
                typeof(ApplicationCommands), typeof(NavigationCommands),
                typeof(EditingCommands), typeof(MediaCommands),
                typeof(ComponentCommands), typeof(SystemCommands),
            };
            Type? hit = null;
            foreach (var holder in holders)
            {
                var property = holder.GetProperty(text,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (property is null || property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true) continue;
                if (!typeof(ICommand).IsAssignableFrom(property.PropertyType)) continue;
                if (hit is not null)
                    Fail<object>(recordOffset, $"command '{text}' in {surface} {display} is ambiguous ({hit.Name} and {holder.Name} both carry it; menu commands take prefix:Type.Member to disambiguate)");
                hit = holder;
            }
            if (hit is null)
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} names an unknown command (menu commands take a canonical ApplicationCommands/NavigationCommands/EditingCommands/MediaCommands/ComponentCommands/SystemCommands name or prefix:Type.Member)");
            object? value;
            try { value = hit!.GetProperty(text, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null); }
            catch (Exception ex)
            {
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} throws reading the canonical command ({ex.GetBaseException().Message})");
                throw new InvalidOperationException("unreachable");
            }
            if (value is ICommand ok) return ok;
            Fail<object>(recordOffset, $"command '{text}' in {surface} {display} is not an engine command");
            throw new InvalidOperationException("unreachable");
        }

        private ICommand ResolveMenuCommand(int recordOffset, string display, string text, string surface = "context-menu entry")
        {
            if (!text.Contains(':'))
                return ResolveCanonicalMenuCommand(recordOffset, display, text, surface);
            var colon = text.IndexOf(':');
            if (colon <= 0 || colon == text.Length - 1)
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} (menu commands take prefix:Type.Member; menu-surface follow-up)");
            var prefix = text[..colon];
            var remainder = text[(colon + 1)..];
            var dot = remainder.LastIndexOf('.');
            if (dot <= 0 || dot == remainder.Length - 1)
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} (menu commands take prefix:Type.Member; menu-surface follow-up)");
            var typeName = remainder[..dot];
            var memberName = remainder[(dot + 1)..];
            string? xmlns = null;
            foreach (var (candidatePrefix, candidateXmlns, _) in _xmlns)
            {
                if (string.Equals(candidatePrefix, prefix, StringComparison.Ordinal)) { xmlns = candidateXmlns; break; }
            }
            if (xmlns is null)
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} names unknown xmlns prefix '{prefix}' (menu-surface follow-up)");
            string? clrNamespace = null;
            var assemblyId = -1;
            foreach (var (candidateXmlns, candidateClr, candidateAsm) in _piMapping)
            {
                if (string.Equals(candidateXmlns, xmlns, StringComparison.Ordinal)) { clrNamespace = candidateClr; assemblyId = candidateAsm; break; }
            }
            if (clrNamespace is null)
                Fail<object>(recordOffset, $"command '{text}' in {surface} {display} has no namespace mapping for '{xmlns}' (menu-surface follow-up)");
            return (ICommand)ResolveConvertedStatic(recordOffset, display, text, assemblyId, clrNamespace + "." + typeName, memberName, typeof(ICommand), surface);
        }

        /// <summary>Reads one MenuItem Header value: a fixed PropertyWithExtension
        /// over the Header member carrying the StaticExtension with a
        /// per-file AttributeInfo id, which names the converted static
        /// (owner TypeInfo plus member) whose value lands as the header (the
        /// staged rows name HandyControl.Properties.Langs.Lang members).
        /// Framework-owned statics stay a precise follow-up.</summary>
        private object? ReadMenuItemHeader(int recordOffset, string display)
        {
            var targetId = ReadI16();
            var extensionId = ReadU16();
            var staticId = ReadI16();
            if (targetId != KnownHeader)
                Fail<object>(recordOffset, $"menu header targets member {targetId} in context-menu entry {display} (menu headers take the Header member; menu-surface follow-up)");
            if (extensionId != ExtStaticExtension)
                Fail<object>(recordOffset, $"menu header holds extension {extensionId} in context-menu entry {display} (menu headers take x:Static; menu-surface follow-up)");
            if (staticId < 0 || staticId >= _attrs.Count)
                Fail<object>(recordOffset, $"menu header static id {staticId} in context-menu entry {display} has no AttributeInfo record");
            var (ownerId, memberName) = _attrs[staticId];
            if (ownerId < 0 || ownerId >= _types.Count)
                Fail<object>(recordOffset, $"menu header static '{memberName}' in context-menu entry {display} names owner id {ownerId} (menu headers take converted statics; menu-surface follow-up)");
            var (asmId, fullName) = _types[ownerId];
            return ResolveConvertedStatic(recordOffset, display, fullName + "." + memberName, asmId, fullName, memberName, expectedType: null);
        }

        /// <summary>Resolves a converted static (assembly id plus full type
        /// name plus member) to its value: the assembly must be loaded under
        /// the BAML short name, the type must carry exactly that full name,
        /// and the member must be a public static property or field. Command
        /// values must implement the engine command contract; anything else
        /// fails precise so a half-built menu never lands.</summary>
        private object? ResolveConvertedStatic(int recordOffset, string display, string text, int assemblyId, string fullName, string memberName, Type? expectedType, string surface = "context-menu entry")
        {
            if (assemblyId < 0 || assemblyId >= _assemblies.Count)
            {
                Fail<object>(recordOffset, $"'{text}' in {surface} {display} references assembly id {assemblyId} (only {_assemblies.Count} AssemblyInfo records)");
                throw new InvalidOperationException("unreachable");
            }
            var full = _assemblies[assemblyId];
            var comma = full.IndexOf(',');
            var shortName = comma < 0 ? full.Trim() : full[..comma].Trim();
            Type? owner = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, shortName, StringComparison.OrdinalIgnoreCase)) continue;
                if (SafeGetType(assembly, fullName) is { } candidate) { owner = candidate; break; }
            }
            if (owner is null)
            {
                Fail<object>(recordOffset, $"'{text}' in {surface} {display} names '{fullName}' (no loadable converted type with that full name in assembly '{shortName}')");
                throw new InvalidOperationException("unreachable");
            }
            const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            object? value;
            var property = owner.GetProperty(memberName, StaticFlags);
            if (property is not null && property.GetIndexParameters().Length == 0 && property.GetMethod?.IsPublic == true)
            {
                try { value = property.GetValue(null); }
                catch (Exception ex)
                {
                    Fail<object>(recordOffset, $"'{text}' in {surface} {display} throws reading the static property ({ex.GetBaseException().Message})");
                    throw new InvalidOperationException("unreachable");
                }
            }
            else
            {
                var field = owner.GetField(memberName, StaticFlags);
                if (field is null)
                {
                    Fail<object>(recordOffset, $"'{text}' in {surface} {display} names no public static property or field '{memberName}' on '{fullName}'");
                    throw new InvalidOperationException("unreachable");
                }
                try { value = field.GetValue(null); }
                catch (Exception ex)
                {
                    Fail<object>(recordOffset, $"'{text}' in {surface} {display} throws reading the static field ({ex.GetBaseException().Message})");
                    throw new InvalidOperationException("unreachable");
                }
            }
            if (expectedType is not null && value is not null && !expectedType.IsInstanceOfType(value))
            {
                Fail<object>(recordOffset, $"'{text}' in {surface} {display} is a {value.GetType().FullName}, not a {expectedType.Name}");
                throw new InvalidOperationException("unreachable");
            }
            return value;
        }
/// <summary>Parses one keyed ItemsPanelTemplate entry (the ElementStart
        /// is consumed): the VisualTree content member, one UniformGrid,
        /// StackPanel or VirtualizingStackPanel visual, closed by ElementEnd. The template lands as a
        /// live engine ItemsPanelTemplate retaining its panel root (simple
        /// values land through the real converters, FocusVisualStyle lands
        /// null from the x:Null complex child like the XAML loader path);
        /// LoadContent stays a precise no-inflation boundary like the
        /// BAML-retained ControlTemplate roots. The staged keyidx 132-135
        /// ButtonGroup entries plus the staged keyidx 139-144 Uniform/Stack/
        /// VirtualizingStackPanel entries are this exact shape (Rows="1" /
        /// Columns="1" plus Orientation="Horizontal" / bare, each with
        /// FocusVisualStyle {x:Null}); non-shared keys are a precise
        /// follow-up. Anything richer fails precise so a half-built resource
        /// never lands.</summary>
        private void ParseItemsPanelTemplateEntry(int offset, object entryKey, string display, bool shared)
        {
            var panel = ParseItemsPanelTemplate(offset, display, display.Trim('\''));
            if (shared) AddEntry(offset, entryKey, display, new ItemsPanelTemplate(panel));
            else Fail<object>(offset, $"keyed entry {display} is non-shared (keyed templates take shared entries; style-surface follow-up)");
        }

        /// <summary>Parses an ItemsPanelTemplate element (its ElementStart is
        /// consumed): the VisualTree content member plus one UniformGrid,
        /// StackPanel, VirtualizingStackPanel, WrapPanel or DockPanel visual,
        /// closed by ElementEnd.</summary>
        private Panel ParseItemsPanelTemplate(int offset, string entryDisplay, string panelDisplay)
        {
            Panel? visual = null;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (visual is null)
                        Fail<object>(offset, $"keyed entry {entryDisplay} holds an ItemsPanelTemplate with no visual (items panels take the VisualTree content plus one UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual; style-surface follow-up)");
                    return visual!;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownVisualTree)
                        Fail<object>(recordOffset, $"content member {member} on items-panel entry {entryDisplay} (items panels take the VisualTree content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    if (visual is not null)
                        Fail<object>(recordOffset, $"second visual on items-panel entry {entryDisplay} (items panels take one visual; style-surface follow-up)");
                    var visualType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (visualType == KnownUniformGrid)
                        visual = ParseItemsPanelUniformGrid(recordOffset, entryDisplay, panelDisplay);
                    else if (visualType == KnownStackPanel)
                        visual = ParseItemsPanelStackPanel(recordOffset, entryDisplay, panelDisplay);
                    else if (visualType == KnownVirtualizingStackPanel)
                        visual = ParseItemsPanelVirtualizingStackPanel(recordOffset, entryDisplay, panelDisplay);
                    else if (visualType == KnownWrapPanel)
                        visual = ParseItemsPanelWrapPanel(recordOffset, entryDisplay, panelDisplay);
                    else if (visualType == KnownDockPanel)
                        visual = ParseItemsPanelDockPanel(recordOffset, entryDisplay, panelDisplay);
                    else
                        Fail<object>(recordOffset, $"element {TargetTypeName(visualType)} on items-panel entry {entryDisplay} (items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"keyed entry {entryDisplay} holds record 0x{record:X2} (items panels take the VisualTree content plus one UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual; style-surface follow-up)");
            }
        }

        /// <summary>Parses a UniformGrid items-panel visual (its ElementStart
        /// is consumed): Rows/Columns PropertyWithConverter simples through
        /// the Int32Converter onto the CLR slots (the staged ButtonGroup
        /// entries carry "1"), the FocusVisualStyle x:Null complex child onto
        /// the CLR slot, closed by ElementEnd. The staged entries carry only
        /// this shape; TemplateBindings, bindings and children are precise
        /// follow-ups.</summary>
        private UniformGrid ParseItemsPanelUniformGrid(int offset, string entryDisplay, string panelDisplay)
        {
            _ = offset;
            var grid = new UniformGrid();
            var haveRows = false;
            var haveColumns = false;
            var haveFocusVisualStyle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return grid;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (IsUniformGridRowsAttribute(attrId))
                    {
                        if (haveRows)
                            Fail<object>(recordOffset, $"duplicate Rows on the items-panel UniformGrid in {panelDisplay} in style entry {entryDisplay} (uniform panels take one Rows plus one Columns; style-surface follow-up)");
                        grid.Rows = ReadItemsPanelInt32(recordOffset, entryDisplay, panelDisplay, "Rows", text, conv);
                        haveRows = true;
                        continue;
                    }
                    if (IsUniformGridColumnsAttribute(attrId))
                    {
                        if (haveColumns)
                            Fail<object>(recordOffset, $"duplicate Columns on the items-panel UniformGrid in {panelDisplay} in style entry {entryDisplay} (uniform panels take one Rows plus one Columns; style-surface follow-up)");
                        grid.Columns = ReadItemsPanelInt32(recordOffset, entryDisplay, panelDisplay, "Columns", text, conv);
                        haveColumns = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"template UniformGrid property id {attrId} in {panelDisplay} in style entry {entryDisplay} (uniform panels take Rows/Columns; style-surface follow-up)");
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveFocusVisualStyle)
                        Fail<object>(recordOffset, $"duplicate FocusVisualStyle on the items-panel UniformGrid in {panelDisplay} in style entry {entryDisplay} (panels take one FocusVisualStyle; style-surface follow-up)");
                    ReadItemsPanelNullStyle(recordOffset, entryDisplay, panelDisplay, "UniformGrid");
                    grid.FocusVisualStyle = null;
                    haveFocusVisualStyle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template UniformGrid holds record 0x{record:X2} in {panelDisplay} in style entry {entryDisplay} (uniform panels take Rows/Columns plus FocusVisualStyle; style-surface follow-up)");
            }
        }

        /// <summary>Parses a StackPanel items-panel visual (its ElementStart is
        /// consumed): the Orientation PropertyWithConverter simple through
        /// the DP store (the staged horizontal ButtonGroup entry carries
        /// "Horizontal" through the EnumConverter; the vertical entry carries
        /// no simples and keeps the Vertical default), the FocusVisualStyle
        /// x:Null complex child onto the CLR slot, closed by ElementEnd.</summary>
        private StackPanel ParseItemsPanelStackPanel(int offset, string entryDisplay, string panelDisplay)
        {
            _ = offset;
            var panel = new StackPanel();
            var haveFocusVisualStyle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, panelDisplay, typeof(StackPanel), attrId);
                    panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, panelDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveFocusVisualStyle)
                        Fail<object>(recordOffset, $"duplicate FocusVisualStyle on the items-panel StackPanel in {panelDisplay} in style entry {entryDisplay} (panels take one FocusVisualStyle; style-surface follow-up)");
                    ReadItemsPanelNullStyle(recordOffset, entryDisplay, panelDisplay, "StackPanel");
                    panel.FocusVisualStyle = null;
                    haveFocusVisualStyle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template StackPanel holds record 0x{record:X2} in {panelDisplay} in style entry {entryDisplay} (items panels take Orientation plus FocusVisualStyle; style-surface follow-up)");
            }
        }
        /// <summary>Parses a VirtualizingStackPanel items-panel visual (its
        /// ElementStart is consumed): the Orientation PropertyWithConverter
        /// simple through the DP store (the staged horizontal entry carries
        /// "Horizontal" through the EnumConverter; the vertical entry carries
        /// no simples and keeps the Vertical default), the FocusVisualStyle
        /// x:Null complex child onto the CLR slot, closed by ElementEnd.</summary>
        private VirtualizingStackPanel ParseItemsPanelVirtualizingStackPanel(int offset, string entryDisplay, string panelDisplay)
        {
            _ = offset;
            var panel = new VirtualizingStackPanel();
            var haveFocusVisualStyle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, panelDisplay, typeof(VirtualizingStackPanel), attrId);
                    panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, panelDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveFocusVisualStyle)
                        Fail<object>(recordOffset, $"duplicate FocusVisualStyle on the items-panel VirtualizingStackPanel in {panelDisplay} in style entry {entryDisplay} (panels take one FocusVisualStyle; style-surface follow-up)");
                    ReadItemsPanelNullStyle(recordOffset, entryDisplay, panelDisplay, "VirtualizingStackPanel");
                    panel.FocusVisualStyle = null;
                    haveFocusVisualStyle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template VirtualizingStackPanel holds record 0x{record:X2} in {panelDisplay} in style entry {entryDisplay} (items panels take Orientation plus FocusVisualStyle; style-surface follow-up)");
            }
        }

        /// <summary>Parses a WrapPanel items-panel visual (its ElementStart
        /// is consumed): the Orientation PropertyWithConverter simple through
        /// the DP store (the staged vertical entry carries "Vertical" through
        /// the EnumConverter over per-file row 54; the horizontal entry
        /// carries none and keeps the Horizontal default), the
        /// HorizontalAlignment/VerticalAlignment PropertyWithConverter
        /// simples through the DP store (both staged entries carry "Center"
        /// through the EnumConverter), the FocusVisualStyle x:Null complex
        /// child onto the CLR slot, closed by ElementEnd.</summary>
        private WrapPanel ParseItemsPanelWrapPanel(int offset, string entryDisplay, string panelDisplay)
        {
            _ = offset;
            var panel = new WrapPanel();
            var haveFocusVisualStyle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, panelDisplay, typeof(WrapPanel), attrId);
                    panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, panelDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveFocusVisualStyle)
                        Fail<object>(recordOffset, $"duplicate FocusVisualStyle on the items-panel WrapPanel in {panelDisplay} in style entry {entryDisplay} (panels take one FocusVisualStyle; style-surface follow-up)");
                    ReadItemsPanelNullStyle(recordOffset, entryDisplay, panelDisplay, "WrapPanel");
                    panel.FocusVisualStyle = null;
                    haveFocusVisualStyle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template WrapPanel holds record 0x{record:X2} in {panelDisplay} in style entry {entryDisplay} (items panels take Orientation/HorizontalAlignment/VerticalAlignment plus FocusVisualStyle; style-surface follow-up)");
            }
        }

        /// <summary>Parses a DockPanel items-panel visual (its ElementStart is
        /// consumed): the LastChildFill PropertyWithConverter simple through
        /// the DP store (the staged entry carries "True" through the
        /// BooleanConverter), the FocusVisualStyle x:Null complex child onto
        /// the CLR slot, closed by ElementEnd.</summary>
        private DockPanel ParseItemsPanelDockPanel(int offset, string entryDisplay, string panelDisplay)
        {
            _ = offset;
            var panel = new DockPanel();
            var haveFocusVisualStyle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, panelDisplay, typeof(DockPanel), attrId);
                    panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, panelDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveFocusVisualStyle)
                        Fail<object>(recordOffset, $"duplicate FocusVisualStyle on the items-panel DockPanel in {panelDisplay} in style entry {entryDisplay} (panels take one FocusVisualStyle; style-surface follow-up)");
                    ReadItemsPanelNullStyle(recordOffset, entryDisplay, panelDisplay, "DockPanel");
                    panel.FocusVisualStyle = null;
                    haveFocusVisualStyle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template DockPanel holds record 0x{record:X2} in {panelDisplay} in style entry {entryDisplay} (items panels take LastChildFill plus FocusVisualStyle; style-surface follow-up)");
            }
        }

        /// <summary>Reads one items-panel Rows/Columns text through the
        /// Int32Converter (the staged ButtonGroup entries carry "1").</summary>
        private int ReadItemsPanelInt32(int offset, string entryDisplay, string panelDisplay, string name, string text, short converter)
        {
            if (converter != ConvInt32)
                Fail<object>(offset, $"template UniformGrid {name} '{text}' in {panelDisplay} in style entry {entryDisplay} uses converter {converter} (row/column counts take the Int32Converter; style-surface follow-up)");
            try { return int.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
            {
                return Fail<int>(offset, $"'{text}' is not an integer for {name} in {panelDisplay} in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
        }

        /// <summary>Reads one items-panel FocusVisualStyle complex value
        /// (PropertyComplexStart holding one NullExtension element, closed
        /// by PropertyComplexEnd, byte-verified against the genuine net10
        /// fixture compiling FocusVisualStyle="{x:Null}"): the attribute must
        /// be the per-file FocusVisualStyle row and the child the
        /// NullExtension element. The value lands as null, matching the live
        /// upstream ButtonGroup panels.</summary>
        private void ReadItemsPanelNullStyle(int offset, string entryDisplay, string panelDisplay, string visual)
        {
            var attrId = ReadI16();
            if (!IsFocusVisualStyleAttribute(attrId))
                Fail<object>(offset, $"complex property id {attrId} on the items-panel {visual} in {panelDisplay} in style entry {entryDisplay} (items panels take the FocusVisualStyle x:Null value; style-surface follow-up)");
            var childRecord = ReadEntryRecord(out var childOffset);
            if (childRecord != 0x03)
                Fail<object>(childOffset, $"FocusVisualStyle on the items-panel {visual} in {panelDisplay} in style entry {entryDisplay} holds record 0x{childRecord:X2} (null values take one NullExtension element; style-surface follow-up)");
            var childType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            if (childType != KnownNullExtension)
                Fail<object>(childOffset, $"FocusVisualStyle on the items-panel {visual} in {panelDisplay} in style entry {entryDisplay} holds <{TargetTypeName(childType)}> (null values take a NullExtension element; style-surface follow-up)");
            var endRecord = ReadEntryRecord(out var endOffset);
            if (endRecord != 0x04)
                Fail<object>(endOffset, $"FocusVisualStyle on the items-panel {visual} in {panelDisplay} in style entry {entryDisplay} holds record 0x{endRecord:X2} (null elements close with ElementEnd; style-surface follow-up)");
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
                Fail<object>(closeOffset, $"FocusVisualStyle on the items-panel {visual} in {panelDisplay} in style entry {entryDisplay} holds record 0x{closeRecord:X2} (complex values close with PropertyComplexEnd; style-surface follow-up)");
        }

        /// <summary>True for the Rows value attribute on an items-panel
        /// UniformGrid (per-file AttributeInfo row owned by UniformGrid).</summary>
        private bool IsUniformGridRowsAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownUniformGrid && string.Equals(name, "Rows", StringComparison.Ordinal);
        }

        /// <summary>True for the Columns value attribute on an items-panel
        /// UniformGrid (per-file AttributeInfo row owned by UniformGrid).</summary>
        private bool IsUniformGridColumnsAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownUniformGrid && string.Equals(name, "Columns", StringComparison.Ordinal);
        }

        /// <summary>True for the FocusVisualStyle complex attribute on an
        /// items-panel visual (per-file AttributeInfo row owned by
        /// FrameworkElement, e.g. the staged row 40).</summary>
        private bool IsFocusVisualStyleAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownFrameworkElement && string.Equals(name, "FocusVisualStyle", StringComparison.Ordinal);
        }

        /// <summary>Parses one keyed SolidColorBrush entry (the ElementStart
        /// is consumed): an optional PresentationOptions Freeze marker plus one
        /// Color value, closed by ElementEnd. The Color travels either as a
        /// DynamicResource PropertyWithExtension (the staged LightPrimaryBrush
        /// run: the per-file SolidColorBrush.Color attribute, the DynamicResource
        /// discriminator, the resource key as a StringInfo id) or as a literal
        /// PropertyWithConverter through the ColorConverter. The DynamicResource
        /// key resolves now against the dictionary-so-far plus Application
        /// resources like the XAML loader resource-entry path (missing keys stay
        /// Transparent, genuine mismatches fail loud); the entry lands as a live
        /// engine SolidColorBrush, frozen when the marker says True. Shared lands
        /// once while non-shared replays the validated bag through a factory so
        /// every lookup materializes a fresh instance, like the gradient run.
        /// Anything richer fails precise so a half-built resource never lands.</summary>
        private void ParseSolidColorBrushEntry(int offset, object entryKey, string display, bool shared)
        {
            string? dynamicKey = null;
            string? literalText = null;
            var freeze = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") freeze = true;
                    continue;
                }
                if (record == 0x24)
                {
                    if (dynamicKey is not null || literalText is not null)
                        Fail<object>(recordOffset, $"duplicate Color on the SolidColorBrush in keyed entry {display}");
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} on the SolidColorBrush in keyed entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner != KnownSolidColorBrush || name != "Color")
                        Fail<object>(recordOffset, $"attribute '{name}' on the SolidColorBrush in keyed entry {display} (solid brushes take the Color value; style-surface follow-up)");
                    if (conv != ConvColor)
                        Fail<object>(recordOffset, $"Color value with converter {conv} on the SolidColorBrush in keyed entry {display} (solid literal colors take the ColorConverter; style-surface follow-up)");
                    literalText = text;
                    continue;
                }
                if (record == 0x23)
                {
                    if (dynamicKey is not null || literalText is not null)
                        Fail<object>(recordOffset, $"duplicate Color on the SolidColorBrush in keyed entry {display}");
                    dynamicKey = ReadSolidBrushDynamicKey(recordOffset, display);
                    continue;
                }
                if (record == 0x04)
                {
                    if (dynamicKey is null && literalText is null)
                        Fail<object>(offset, $"keyed entry {display} holds a SolidColorBrush with no Color (solid brushes take one Color value; style-surface follow-up)");
                    var key = dynamicKey;
                    var colorText = literalText;
                    var frozen = freeze;
                    SolidColorBrush Build()
                    {
                        Color color = key is not null
                            ? ResolveSolidBrushColor(offset, display, key)
                            : (Color)ParseColorEntry(offset, display, colorText!);
                        var brush = new SolidColorBrush(color);
                        if (frozen) brush.Freeze();
                        return brush;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Build());
                    else AddDeferredEntry(offset, entryKey, display, Build);
                    return;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the SolidColorBrush in keyed entry {display} (solid static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the SolidColorBrush in keyed entry {display} (solid static values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a SolidColorBrush (solid brushes take the Color value; style-surface follow-up)");
            }
        }

        /// <summary>Reads the DynamicResource Color of a keyed SolidColorBrush
        /// entry (fixed PropertyWithExtension: the per-file SolidColorBrush.Color
        /// attribute, the DynamicResource discriminator, the resource key as a
        /// StringInfo id; byte-verified on the staged LightPrimaryBrush run and
        /// a genuine net10.0-windows fixture compiling the same upstream shape).
        /// Anything else fails precise so a half-built resource never lands.</summary>
        private string ReadSolidBrushDynamicKey(int offset, string display)
        {
            var targetId = ReadI16();
            var extensionId = ReadU16();
            var keyId = ReadU16();
            if (targetId < 0 || targetId >= _attrs.Count)
                Fail<object>(offset, $"solid brush target id {targetId} in keyed entry {display} has no AttributeInfo record");
            var (owner, name) = _attrs[targetId];
            if (owner != KnownSolidColorBrush || name != "Color")
                Fail<object>(offset, $"attribute '{name}' in keyed entry {display} (solid DynamicResource colors take the SolidColorBrush Color; style-surface follow-up)");
            if (extensionId != ExtDynamicResource)
                Fail<object>(offset, $"extension {extensionId} on the Color in keyed entry {display} (solid DynamicResource colors take the DynamicResource extension; style-surface follow-up)");
            if (keyId >= _strings.Count)
                Fail<object>(offset, $"resource key id {keyId} on the Color in keyed entry {display} has no StringInfo record");
            return _strings[keyId];
        }

        /// <summary>Resolves a keyed SolidColorBrush DynamicResource color key
        /// now: the dictionary-so-far first (same-file earlier keys plus merges),
        /// then Application resources, like the XAML loader resource-entry path.
        /// Missing keys stay Transparent; genuine mismatches fail loud. The
        /// gradient-stop run shares this resolver (same 189 discriminator,
        /// same resolve-now rule).</summary>
        private Color ResolveSolidBrushColor(int offset, string display, string key)
        {
            if (TryFindResourceInScope(key, out var resource) && resource is not null)
                return CoerceSolidBrushColor(offset, display, key, resource);
            return Colors.Transparent;
        }

        /// <summary>Coerces a resolved DynamicResource color to an engine Color
        /// (Color/SolidColorBrush/packed-uint/text all land, mirroring the XAML
        /// loader resource-entry rule); anything else fails loud.</summary>
        private Color CoerceSolidBrushColor(int offset, string display, string key, object? resource) =>
            resource switch
            {
                null => Colors.Transparent,
                Color color => color,
                SolidColorBrush solid => solid.Color,
                uint rgba => StyleValueConverter.ColorFromRgba(rgba),
                string text => (Color)ParseColorEntry(offset, display, text),
                _ => Fail<Color>(offset, $"DynamicResource '{key}' for keyed entry {display} is a {resource.GetType().Name}, not a color"),
            };

        /// <summary>Parses one keyed LinearGradientBrush entry (the
        /// ElementStart is consumed): an optional Freeze marker plus
        /// StartPoint/EndPoint PropertyWithConverter values over the
        /// per-file brush rows plus the GradientStops content member with
        /// GradientStop children carrying Color/Offset
        /// PropertyWithConverter values or DynamicResource Color
        /// PropertyWithExtension values (the same 189 discriminator as the
        /// SolidColorBrush run), closed by ElementEnd.
        /// Points convert through the PointConverter, colors through the
        /// ColorConverter and offsets through the DoubleConverter like the
        /// XAML loader path. The brush lands as a live engine
        /// LinearGradientBrush with its GradientStops (shared lands once
        /// while non-shared replays the validated value bags through a
        /// factory so every lookup materializes a fresh instance, resolving
        /// the live DynamicResource seed each time, like the storyboard
        /// run). The staged keyidx 137
        /// 'ProgressBarIndicatorAnimatedFill' entry is this exact shape
        /// (StartPoint "0,0" / EndPoint "1,0" plus four GradientStops
        /// #00FFFFFF/0, #60FFFFFF/0.4, #60FFFFFF/0.6, #00FFFFFF/1).
        /// The staged keyidx 153 'ColorPickerRainbowBrush' entry is the
        /// defaults shape (no StartPoint/EndPoint records, Color-only
        /// first stop): absent points land at the WPF defaults
        /// (StartPoint (0,0), EndPoint (1,1), live-verified against
        /// net10.0-windows PresentationCore) and a missing stop Offset
        /// at 0.0, because BAML omits default values (proven by a
        /// genuine net10.0-windows fixture compiling the exact upstream
        /// block, which emits the same record shapes with no point
        /// records and a Color-only first stop).
        /// Anything richer (StaticResource values, markup extensions on the
        /// brush itself, non-GradientStop children) fails precise so a
        /// half-built resource never lands.</summary>
        private void ParseLinearGradientBrushEntry(int offset, object entryKey, string display, bool shared)
        {
            Point? start = null;
            Point? end = null;
            var stops = new List<GradientStopBag>();
            var frozen = false;
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") frozen = true;
                    continue;
                }
                if (record == 0x04)
                {
                    // Absent points land at the WPF defaults (StartPoint
                    // (0,0), EndPoint (1,1), live-verified on
                    // net10.0-windows): BAML omits default values, so
                    // the staged rainbow entry carries no point records.
                    if (!haveContent)
                        Fail<object>(offset, $"keyed entry {display} holds a LinearGradientBrush with no GradientStops (gradient brushes take the GradientStops content plus GradientStop children; style-surface follow-up)");
                    if (stops.Count == 0)
                        Fail<object>(offset, $"keyed entry {display} holds a LinearGradientBrush with no GradientStop (gradient brushes take one GradientStop per color step; style-surface follow-up)");
                    LinearGradientBrush Build()
                    {
                        var brush = new LinearGradientBrush
                        {
                            StartPoint = start ?? new Point(0, 0),
                            EndPoint = end ?? new Point(1, 1),
                        };
                        foreach (var bag in stops)
                        {
                            var stop = new GradientStop
                            {
                                Color = bag.DynamicKey is not null
                                    ? ResolveSolidBrushColor(offset, display, bag.DynamicKey)
                                    : bag.Literal!.Value,
                                Offset = bag.Offset,
                            };
                            if (bag.Frozen) stop.Freeze();
                            brush.GradientStops.Add(stop);
                        }
                        if (frozen) brush.Freeze();
                        return brush;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Build());
                    else AddDeferredEntry(offset, entryKey, display, Build);
                    return;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in gradient entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownLinearGradientBrush && (name == "StartPoint" || name == "EndPoint"))
                    {
                        if (conv != ConvPoint)
                            Fail<object>(recordOffset, $"{name} value with converter {conv} in gradient entry {display} (gradient points take the PointConverter; style-surface follow-up)");
                        Point point;
                        try { point = Point.Parse(text); }
                        catch (FormatException ex)
                        {
                            Fail<object>(recordOffset, $"'{text}' is not a Point for {name} in gradient entry {display} ({ex.Message})");
                            throw new InvalidOperationException("unreachable");
                        }
                        if (name == "StartPoint")
                        {
                            if (start.HasValue)
                                Fail<object>(recordOffset, $"duplicate StartPoint on the LinearGradientBrush in gradient entry {display}");
                            start = point;
                        }
                        else
                        {
                            if (end.HasValue)
                                Fail<object>(recordOffset, $"duplicate EndPoint on the LinearGradientBrush in gradient entry {display}");
                            end = point;
                        }
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' in gradient entry {display} (gradient brushes take StartPoint/EndPoint plus the GradientStops content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownGradientStopsContent)
                        Fail<object>(recordOffset, $"content member {member} on gradient entry {display} (gradient brushes take the GradientStops content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate GradientStops content on gradient entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveContent)
                        Fail<object>(recordOffset, $"GradientStop precedes the GradientStops content on gradient entry {display} (BAML emits GradientStops first; style-surface follow-up)");
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownGradientStop)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} inside gradient entry {display} (gradient brushes take GradientStop children; style-surface follow-up)");
                    stops.Add(ParseLinearGradientStop(recordOffset, display));
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the LinearGradientBrush in gradient entry {display} (gradient static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the LinearGradientBrush in gradient entry {display} (gradient static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the LinearGradientBrush in gradient entry {display} (gradient markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a LinearGradientBrush (gradient brushes take StartPoint/EndPoint plus the GradientStops content with GradientStop children; style-surface follow-up)");
            }
        }

        /// <summary>Validated GradientStop payload inside a keyed
        /// LinearGradientBrush entry: either a literal Color or a
        /// DynamicResource key (resolved now like the SolidColorBrush run:
        /// the dictionary-so-far first, then Application resources, missing
        /// keys staying Transparent), plus the Offset and the stop Freeze
        /// marker. Non-shared entries replay the bag through a factory so
        /// every lookup resolves the live seed.</summary>
        private sealed class GradientStopBag
        {
            public Color? Literal;
            public string? DynamicKey;
            public double Offset;
            public bool Frozen;
        }

        /// <summary>Reads the DynamicResource Color of a GradientStop
        /// (fixed PropertyWithExtension: the per-file GradientStop.Color
        /// attribute, the DynamicResource discriminator, the resource key as
        /// a StringInfo id; byte-verified on the staged PrimaryBrush run and
        /// a genuine net10.0-windows fixture compiling the same upstream
        /// shape). Anything else fails precise so a half-built resource
        /// never lands.</summary>
        private string ReadGradientStopDynamicKey(int offset, string display)
        {
            var targetId = ReadI16();
            var extensionId = ReadU16();
            var keyId = ReadU16();
            if (targetId < 0 || targetId >= _attrs.Count)
                Fail<object>(offset, $"gradient stop target id {targetId} in gradient entry {display} has no AttributeInfo record");
            var (owner, name) = _attrs[targetId];
            if (owner != KnownGradientStop || name != "Color")
                Fail<object>(offset, $"attribute '{name}' in gradient entry {display} (gradient DynamicResource colors take the GradientStop Color; style-surface follow-up)");
            if (extensionId != ExtDynamicResource)
                Fail<object>(offset, $"extension {extensionId} on the GradientStop Color in gradient entry {display} (gradient DynamicResource colors take the DynamicResource extension; style-surface follow-up)");
            if (keyId >= _strings.Count)
                Fail<object>(offset, $"resource key id {keyId} on the GradientStop in gradient entry {display} has no StringInfo record");
            return _strings[keyId];
        }

        /// <summary>Parses one GradientStop child inside a keyed
        /// LinearGradientBrush entry (its ElementStart is consumed): an
        /// optional Freeze marker plus Color and Offset
        /// PropertyWithConverter values over the per-file stop rows or a
        /// DynamicResource Color PropertyWithExtension over the per-file
        /// GradientStop.Color row, closed by ElementEnd. A missing Offset
        /// lands at the WPF default 0.0 (BAML omits default values). Values
        /// convert eagerly with entry offsets, so the first failure is
        /// precise.</summary>
        private GradientStopBag ParseLinearGradientStop(int offset, string display)
        {
            Color? literal = null;
            string? dynamicKey = null;
            double? stopOffset = null;
            var frozen = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (literal is null && dynamicKey is null)
                        return Fail<GradientStopBag>(offset, $"GradientStop in gradient entry {display} has no Color (gradient stops take Color plus Offset; style-surface follow-up)");
                    // A missing Offset lands at the WPF default 0.0
                    // (live-verified on net10.0-windows): BAML omits
                    // default values, so the staged rainbow first stop
                    // carries Color only.
                    return new GradientStopBag
                    {
                        Literal = literal,
                        DynamicKey = dynamicKey,
                        Offset = stopOffset ?? 0.0,
                        Frozen = frozen,
                    };
                }
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") frozen = true;
                    continue;
                }
                if (record == 0x23)
                {
                    if (literal is not null || dynamicKey is not null)
                        Fail<object>(recordOffset, $"duplicate Color on the GradientStop in gradient entry {display}");
                    dynamicKey = ReadGradientStopDynamicKey(recordOffset, display);
                    continue;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} on the GradientStop in gradient entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownGradientStop && name == "Color")
                    {
                        if (literal is not null || dynamicKey is not null)
                            Fail<object>(recordOffset, $"duplicate Color on the GradientStop in gradient entry {display}");
                        if (conv != ConvColor)
                            Fail<object>(recordOffset, $"Color value with converter {conv} on the GradientStop in gradient entry {display} (gradient colors take the ColorConverter; style-surface follow-up)");
                        literal = (Color)ParseColorEntry(recordOffset, display, text);
                        continue;
                    }
                    if (owner == KnownGradientStop && name == "Offset")
                    {
                        if (stopOffset.HasValue)
                            Fail<object>(recordOffset, $"duplicate Offset on the GradientStop in gradient entry {display}");
                        if (conv != ConvDouble)
                            Fail<object>(recordOffset, $"Offset value with converter {conv} on the GradientStop in gradient entry {display} (gradient offsets take the DoubleConverter; style-surface follow-up)");
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                            Fail<object>(recordOffset, $"'{text}' is not an Offset for the GradientStop in gradient entry {display}");
                        stopOffset = number;
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' on the GradientStop in gradient entry {display} (gradient stops take Color/Offset; style-surface follow-up)");
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the GradientStop in gradient entry {display} (gradient static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the GradientStop in gradient entry {display} (gradient static values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"GradientStop in gradient entry {display} holds record 0x{record:X2} (gradient stops take Color plus Offset; style-surface follow-up)");
            }
        }

        /// <summary>Validated DropShadowEffect payload inside a keyed entry:
        /// optional doubles plus the RenderingBias plus either a literal Color
        /// or a StaticResource key (resolved now, like the SolidColorBrush
        /// run). Non-shared entries replay the bag through a factory so every
        /// lookup resolves the live seed.</summary>
        private sealed class DropShadowEffectBag
        {
            public double? BlurRadius;
            public double? ShadowDepth;
            public double? Direction;
            public double? Opacity;
            public RenderingBias? Bias;
            public Color? LiteralColor;
            public string? StaticColorKey;
            public bool Frozen;
        }

        /// <summary>Parses one keyed DropShadowEffect entry (the ElementStart
        /// is consumed): an optional Freeze marker plus BlurRadius/ShadowDepth/
        /// Direction/Opacity PropertyWithConverter values through the
        /// DoubleConverter, the RenderingBias PropertyWithConverter through
        /// the EnumConverter, and the Color as a PropertyWithStaticResourceId
        /// over the per-file Color row (indexing the entry key-block static
        /// declarations, resolved now against the dictionary-so-far plus
        /// Application resources like the XAML loader resource-entry path) or
        /// as a literal PropertyWithConverter through the ColorConverter,
        /// closed by ElementEnd. Absent values land at the WPF defaults
        /// (BlurRadius/ShadowDepth 5, Direction 315, Color opaque black,
        /// Opacity 1, RenderingBias Performance, all live-verified against
        /// net10.0-windows PresentationCore), because BAML omits unset
        /// values. The effect lands as a live engine DropShadowEffect (shared
        /// lands once while non-shared replays the validated bag through a
        /// factory so every lookup resolves the live static seed, like the
        /// brush runs). The staged keyidx 231-235 EffectShadow1-5 entries are
        /// this exact shape (frozen, e.g. Shadow1 5/1/270/static
        /// EffectShadowColor/.2/Performance); anything richer (optimized
        /// statics, markup extensions, children) fails precise so a half-built
        /// resource never lands.</summary>
        private void ParseDropShadowEffectEntry(int offset, object entryKey, string display, bool shared, short effectTypeId)
        {
            var bag = new DropShadowEffectBag();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") bag.Frozen = true;
                    continue;
                }
                if (record == 0x04)
                {
                    DropShadowEffect Build()
                    {
                        var effect = new DropShadowEffect
                        {
                            BlurRadius = bag.BlurRadius ?? 5,
                            ShadowDepth = bag.ShadowDepth ?? 5,
                            Direction = bag.Direction ?? 315,
                            Color = bag.LiteralColor ?? (bag.StaticColorKey is not null
                                ? ResolveEffectStaticColor(offset, display, bag.StaticColorKey)
                                : Color.FromArgb(0xFF, 0, 0, 0)),
                            Opacity = bag.Opacity ?? 1,
                            RenderingBias = bag.Bias ?? RenderingBias.Performance,
                        };
                        if (bag.Frozen) effect.Freeze();
                        return effect;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Build());
                    else AddDeferredEntry(offset, entryKey, display, Build);
                    return;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    ReadEffectSimple(recordOffset, display, effectTypeId, bag, attrId, text, conv);
                    continue;
                }
                if (record == 0x38)
                {
                    if (_pos + 4 > bytes.Length)
                        Fail<object>(recordOffset, $"truncated BAML StaticResource value in keyed entry {display}");
                    var colorAttr = ReadI16();
                    var staticId = (int)ReadU16();
                    if (bag.LiteralColor.HasValue || bag.StaticColorKey is not null)
                        Fail<object>(recordOffset, $"duplicate Color on the DropShadowEffect in keyed entry {display}");
                    bag.StaticColorKey = ReadEffectStaticColorKey(recordOffset, display, effectTypeId, colorAttr, staticId);
                    continue;
                }
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the DropShadowEffect in keyed entry {display} (effect static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the DropShadowEffect in keyed entry {display} (effect markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a DropShadowEffect (drop shadows take BlurRadius/ShadowDepth/Direction/Color/Opacity/RenderingBias; style-surface follow-up)");
            }
        }

        /// <summary>Reads one PropertyWithConverter value inside a keyed
        /// DropShadowEffect entry: a double (BlurRadius/ShadowDepth/Direction/
        /// Opacity through the DoubleConverter), the RenderingBias enum
        /// through the EnumConverter, or a literal Color through the
        /// ColorConverter, each landed into the validated bag with duplicate
        /// detection. The attribute must be a per-file row owned by the entry
        /// own effect type.</summary>
        private void ReadEffectSimple(int recordOffset, string display, short effectTypeId, DropShadowEffectBag bag, short attrId, string text, short conv)
        {
            if (attrId < 0 || attrId >= _attrs.Count)
                Fail<object>(recordOffset, $"attribute id {attrId} in shadow entry {display} has no AttributeInfo record");
            var (owner, name) = _attrs[attrId];
            if (owner != effectTypeId)
                Fail<object>(recordOffset, $"attribute '{name}' in shadow entry {display} is owned by {TypeKeyName(owner)} (drop shadows take effect-owned values; style-surface follow-up)");
            if (name == "BlurRadius" || name == "ShadowDepth" || name == "Direction" || name == "Opacity")
            {
                if (conv != ConvDouble)
                    Fail<object>(recordOffset, $"{name} value with converter {conv} in shadow entry {display} (shadow doubles take the DoubleConverter; style-surface follow-up)");
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    Fail<object>(recordOffset, $"'{text}' is not a number for {name} in shadow entry {display}");
                if (name == "BlurRadius")
                {
                    if (bag.BlurRadius.HasValue)
                        Fail<object>(recordOffset, $"duplicate BlurRadius on the DropShadowEffect in shadow entry {display}");
                    bag.BlurRadius = number;
                }
                else if (name == "ShadowDepth")
                {
                    if (bag.ShadowDepth.HasValue)
                        Fail<object>(recordOffset, $"duplicate ShadowDepth on the DropShadowEffect in shadow entry {display}");
                    bag.ShadowDepth = number;
                }
                else if (name == "Direction")
                {
                    if (bag.Direction.HasValue)
                        Fail<object>(recordOffset, $"duplicate Direction on the DropShadowEffect in shadow entry {display}");
                    bag.Direction = number;
                }
                else
                {
                    if (bag.Opacity.HasValue)
                        Fail<object>(recordOffset, $"duplicate Opacity on the DropShadowEffect in shadow entry {display}");
                    bag.Opacity = number;
                }
                return;
            }
            if (name == "RenderingBias")
            {
                if (conv != ConvEnum)
                    Fail<object>(recordOffset, $"RenderingBias value with converter {conv} in shadow entry {display} (shadow RenderingBias takes the EnumConverter; style-surface follow-up)");
                if (bag.Bias.HasValue)
                    Fail<object>(recordOffset, $"duplicate RenderingBias on the DropShadowEffect in shadow entry {display}");
                try
                {
                    bag.Bias = Enum.Parse<RenderingBias>(text, ignoreCase: false);
                }
                catch (ArgumentException)
                {
                    Fail<object>(recordOffset, $"'{text}' is not a RenderingBias for shadow entry {display}");
                }
                return;
            }
            if (name == "Color")
            {
                if (conv != ConvColor)
                    Fail<object>(recordOffset, $"Color value with converter {conv} in shadow entry {display} (shadow literal colors take the ColorConverter; style-surface follow-up)");
                if (bag.LiteralColor.HasValue || bag.StaticColorKey is not null)
                    Fail<object>(recordOffset, $"duplicate Color on the DropShadowEffect in shadow entry {display}");
                bag.LiteralColor = (Color)ParseColorEntry(recordOffset, display, text);
                return;
            }
            Fail<object>(recordOffset, $"attribute '{name}' in shadow entry {display} (drop shadows take BlurRadius/ShadowDepth/Direction/Color/Opacity/RenderingBias; style-surface follow-up)");
        }

        /// <summary>Reads the StaticResource Color of a keyed DropShadowEffect
        /// entry (fixed PropertyWithStaticResourceId: the per-file Color
        /// attribute plus the key-block static id, byte-verified on the staged
        /// EffectShadow1-5 entries plus a genuine net10.0-windows fixture
        /// compiling the upstream Themes/Basic/Effects.xaml block, both
        /// emitting the fixed 5-byte shape with the key-block
        /// OptimizedStaticResource declaration). Returns the resource key
        /// name for resolve-now landing.</summary>
        private string ReadEffectStaticColorKey(int offset, string display, short effectTypeId, short colorAttr, int staticId)
        {
            if (colorAttr < 0 || colorAttr >= _attrs.Count)
                Fail<object>(offset, $"static color target id {colorAttr} in shadow entry {display} has no AttributeInfo record");
            var (owner, name) = _attrs[colorAttr];
            if (owner != effectTypeId || name != "Color")
                Fail<object>(offset, $"attribute '{name}' in shadow entry {display} (effect static colors take the DropShadowEffect Color; style-surface follow-up)");
            if (_entryStatics is null || staticId < 0 || staticId >= _entryStatics.Count)
                Fail<object>(offset, $"StaticResource id {staticId} in shadow entry {display} has no StaticResource index (only {(_entryStatics is null ? 0 : _entryStatics.Count)} indexed static values; style-surface follow-up)");
            var (flags, id, key) = _entryStatics![staticId];
            _ = id;
            if (flags != 0 || string.IsNullOrEmpty(key))
                Fail<object>(offset, $"StaticResource id {staticId} in shadow entry {display} names a non-string static resource (effect static colors take string-keyed resources; style-surface follow-up)");
            return key;
        }

        /// <summary>Resolves a keyed DropShadowEffect StaticResource color key
        /// now: the dictionary-so-far first (same-file earlier keys plus merges),
        /// then Application resources, like the XAML loader resource-entry path.
        /// Unlike DynamicResource, StaticResource must resolve: a missing key
        /// fails loud, and genuine mismatches fail loud.</summary>
        private Color ResolveEffectStaticColor(int offset, string display, string key)
        {
            if (TryFindResourceInScope(key, out var resource) && resource is not null)
                return CoerceEffectStaticColor(offset, display, key, resource);
            return Fail<Color>(offset, $"StaticResource '{key}' for keyed entry {display} has no resource (static colors resolve eagerly; style-surface follow-up)");
        }

        /// <summary>Coerces a resolved StaticResource color to an engine Color
        /// (Color/SolidColorBrush/packed-uint/text all land, mirroring the XAML
        /// loader resource-entry rule); anything else fails loud.</summary>
        private Color CoerceEffectStaticColor(int offset, string display, string key, object? resource) =>
            resource switch
            {
                null => Fail<Color>(offset, $"StaticResource '{key}' for keyed entry {display} has no resource (static colors resolve eagerly; style-surface follow-up)"),
                Color color => color,
                SolidColorBrush solid => solid.Color,
                uint rgba => StyleValueConverter.ColorFromRgba(rgba),
                string text => (Color)ParseColorEntry(offset, display, text),
                _ => Fail<Color>(offset, $"StaticResource '{key}' for keyed entry {display} is a {resource.GetType().Name}, not a color"),
            };

        /// <summary>Parses one keyed DrawingBrush entry (the ElementStart is
        /// consumed): Viewport/ViewportUnits/Stretch/TileMode
        /// PropertyWithConverter values over the per-file TileBrush rows plus
        /// the Drawing PropertyComplexStart holding one DrawingGroup, closed
        /// by ElementEnd. Viewport converts through the RectConverter and the
        /// other three through the EnumConverter like the XAML loader path;
        /// absent values land at the engine TileBrush defaults (the XAML
        /// loader lands the same CLR defaults for omitted attributes; note
        /// the live-verified WPF TileMode default is None while the engine
        /// keeps Tile, held by an existing ConvertedGeometryInteractionTests
        /// pin, so the engine default stays). The brush lands as a live
        /// engine DrawingBrush with a DrawingGroup of GeometryDrawings
        /// (shared lands once while non-shared replays the validated bags
        /// through a factory so every lookup materializes a fresh instance,
        /// like the gradient run). The staged keyidx 154
        /// 'ColorPickerOpacityBrush' entry is this exact shape (Viewport
        /// "0,0,12,11" with Absolute units, Stretch None, TileMode Tile plus
        /// a DrawingGroup of two GeometryDrawings: "#d0cec7" over a two-rect
        /// GeometryGroup and "#e7e7e2" over a single RectangleGeometry).
        /// Anything richer fails precise so a half-built resource never
        /// lands.</summary>
        /// <summary>Validated GeometryDrawing payload inside a keyed
        /// DrawingBrush entry: the brush color plus a single rect, a rect
        /// group or path-data figures (the staged brushes carry one shape
        /// each).</summary>
        private sealed class PathDrawing
        {
            public Color Brush;
            public List<Rect> Group = new();
            public Rect? Single;
            public List<PathFigureData>? Path;
            public bool Frozen;
        }

        /// <summary>Validated path-data figure: the figure start plus its
        /// line points (the staged brushes carry Move/Line/Close only, all
        /// figures closed).</summary>
        private sealed class PathFigureData
        {
            public Point Start;
            public List<Point> Points = new();
            public bool Closed;
        }

        private void ParseDrawingBrushEntry(int offset, object entryKey, string display, bool shared)
        {
            Rect? viewport = null;
            BrushMappingMode? units = null;
            Stretch? stretch = null;
            TileMode? tile = null;
            var drawings = new List<PathDrawing>();
            var haveDrawing = false;
            var brushFrozen = false;
            var groupFrozen = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveDrawing)
                        Fail<object>(offset, $"keyed entry {display} holds a DrawingBrush with no Drawing (drawing brushes take Viewport/ViewportUnits/Stretch/TileMode plus the Drawing complex; style-surface follow-up)");
                    if (drawings.Count == 0)
                        Fail<object>(offset, $"keyed entry {display} holds a DrawingBrush with no GeometryDrawing (drawing groups take one GeometryDrawing per checker step; style-surface follow-up)");
                    DrawingBrush Build()
                    {
                        var group = new DrawingGroup();
                        foreach (var drawing in drawings)
                        {
                            Geometry geometry;
                            if (drawing.Single.HasValue)
                            {
                                geometry = new RectangleGeometry(drawing.Single.Value);
                            }
                            else if (drawing.Group.Count > 0)
                            {
                                var members = new GeometryGroup();
                                foreach (var rect in drawing.Group) members.Children.Add(new RectangleGeometry(rect));
                                geometry = members;
                            }
                            else if (drawing.Path is not null)
                            {
                                geometry = BuildPathGeometry(drawing.Path);
                            }
                            else
                            {
                                throw new InvalidOperationException("unreachable: drawings carry a geometry");
                            }
                            var child = new GeometryDrawing { Brush = new SolidColorBrush(drawing.Brush), Geometry = geometry };
                            if (drawing.Frozen) child.Freeze();
                            group.Children.Add(child);
                        }
                        var brush = new DrawingBrush
                        {
                            Viewport = viewport ?? new Rect(0, 0, 1, 1),
                            ViewportUnits = units ?? BrushMappingMode.RelativeToBoundingBox,
                            Stretch = stretch ?? Stretch.Fill,
                            TileMode = tile ?? TileMode.Tile,
                            Drawing = group,
                        };
                        if (brushFrozen) brush.Freeze();
                        if (groupFrozen) group.Freeze();
                        return brush;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Build());
                    else AddDeferredEntry(offset, entryKey, display, Build);
                    return;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in drawing entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownTileBrush && name == "Viewport")
                    {
                        if (viewport.HasValue)
                            Fail<object>(recordOffset, $"duplicate Viewport on the DrawingBrush in drawing entry {display}");
                        if (conv != ConvRect)
                            Fail<object>(recordOffset, $"Viewport value with converter {conv} in drawing entry {display} (drawing viewports take the RectConverter; style-surface follow-up)");
                        try { viewport = RectangleGeometry.ParseRect(text); }
                        catch (FormatException ex)
                        {
                            Fail<object>(recordOffset, $"'{text}' is not a Rect for Viewport in drawing entry {display} ({ex.Message})");
                        }
                        continue;
                    }
                    if (owner == KnownTileBrush && (name == "ViewportUnits" || name == "Stretch" || name == "TileMode"))
                    {
                        if (conv != ConvEnum)
                            Fail<object>(recordOffset, $"{name} value with converter {conv} in drawing entry {display} (drawing tiling knobs take the EnumConverter; style-surface follow-up)");
                        try
                        {
                            if (name == "ViewportUnits")
                            {
                                if (units.HasValue)
                                    Fail<object>(recordOffset, $"duplicate ViewportUnits on the DrawingBrush in drawing entry {display}");
                                units = (BrushMappingMode)Enum.Parse(typeof(BrushMappingMode), text, ignoreCase: false);
                            }
                            else if (name == "Stretch")
                            {
                                if (stretch.HasValue)
                                    Fail<object>(recordOffset, $"duplicate Stretch on the DrawingBrush in drawing entry {display}");
                                stretch = (Stretch)Enum.Parse(typeof(Stretch), text, ignoreCase: false);
                            }
                            else
                            {
                                if (tile.HasValue)
                                    Fail<object>(recordOffset, $"duplicate TileMode on the DrawingBrush in drawing entry {display}");
                                tile = (TileMode)Enum.Parse(typeof(TileMode), text, ignoreCase: false);
                            }
                        }
                        catch (ArgumentException)
                        {
                            Fail<object>(recordOffset, $"'{text}' is not a {name} value in drawing entry {display} (viewport units take Absolute/RelativeToBoundingBox, Stretch takes None/Fill/Uniform/UniformToFill and TileMode takes None/FlipX/FlipY/FlipXY/Tile)");
                        }
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' in drawing entry {display} (drawing brushes take Viewport/ViewportUnits/Stretch/TileMode plus the Drawing complex; style-surface follow-up)");
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveDrawing)
                        Fail<object>(recordOffset, $"duplicate Drawing on the DrawingBrush in drawing entry {display} (drawing brushes take one DrawingGroup; style-surface follow-up)");
                    var complexAttr = ReadI16();
                    if (complexAttr < 0 || complexAttr >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {complexAttr} in drawing entry {display} has no AttributeInfo record");
                    var (complexOwner, complexName) = _attrs[complexAttr];
                    if (complexOwner != KnownDrawingBrush || complexName != "Drawing")
                        Fail<object>(recordOffset, $"attribute '{complexName}' in drawing entry {display} (drawing brushes take the Drawing complex; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"Drawing child holds record 0x{childRecord:X2} in drawing entry {display} (the Drawing complex takes one DrawingGroup; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownDrawingGroup)
                        Fail<object>(childOffset, $"element {WellKnownName(childType)} in drawing entry {display} (the Drawing complex takes one DrawingGroup; style-surface follow-up)");
                    var (children, frozen) = ParseDrawingGroupChildren(childOffset, display);
                    drawings.AddRange(children);
                    groupFrozen = frozen;
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"Drawing complex in drawing entry {display} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    haveDrawing = true;
                    continue;
                }
                if (record == 0x34)
                {
                    // PresentationOptions Freeze marker (the staged
                    // SortByCategory brush freezes the DrawingBrush itself,
                    // like its DrawingGroup and GeometryDrawings): a no-op
                    // unless the value is True, mirroring the scalar-entry
                    // Freeze path.
                    if (ReadPresentationOptions(recordOffset) == "True") brushFrozen = true;
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the DrawingBrush in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the DrawingBrush in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the DrawingBrush in drawing entry {display} (drawing markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a DrawingBrush (drawing brushes take Viewport/ViewportUnits/Stretch/TileMode plus the Drawing complex; style-surface follow-up)");
            }
        }

        /// <summary>Parses the DrawingGroup child of a keyed DrawingBrush
        /// entry (its ElementStart is consumed): the Children content member
        /// plus GeometryDrawing children, closed by ElementEnd. Each child
        /// lands as a validated (brush color, group rects or single rect)
        /// bag; the instances materialize in the entry factory so non-shared
        /// keys replay them per lookup.</summary>
        private (List<PathDrawing> Drawings, bool Frozen) ParseDrawingGroupChildren(int offset, string display)
        {
            var drawings = new List<PathDrawing>();
            var haveContent = false;
            var frozen = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveContent)
                        Fail<object>(offset, $"keyed entry {display} holds a DrawingGroup with no Children (drawing groups take the Children content plus GeometryDrawing children; style-surface follow-up)");
                    if (drawings.Count == 0)
                        Fail<object>(offset, $"keyed entry {display} holds a DrawingGroup with no GeometryDrawing (drawing groups take one GeometryDrawing per checker step; style-surface follow-up)");
                    return (drawings, frozen);
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownDrawingChildren)
                        Fail<object>(recordOffset, $"content member {member} on drawing entry {display} (drawing groups take the Children content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate Children content on drawing entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveContent)
                        Fail<object>(recordOffset, $"GeometryDrawing precedes the Children content on drawing entry {display} (BAML emits Children first; style-surface follow-up)");
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownGeometryDrawing)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} inside drawing entry {display} (drawing groups take GeometryDrawing children; style-surface follow-up)");
                    drawings.Add(ParseGeometryDrawing(recordOffset, display));
                    continue;
                }
                if (record == 0x34)
                {
                    // PresentationOptions Freeze marker (the staged
                    // SortByCategory group freezes like its brush and
                    // drawings): a no-op unless the value is True,
                    // mirroring the scalar-entry Freeze path.
                    if (ReadPresentationOptions(recordOffset) == "True") frozen = true;
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the DrawingGroup in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the DrawingGroup in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the DrawingGroup in drawing entry {display} (drawing markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"DrawingGroup in drawing entry {display} holds record 0x{record:X2} (drawing groups take the Children content plus GeometryDrawing children; style-surface follow-up)");
            }
        }

        /// <summary>Parses one GeometryDrawing child inside a keyed
        /// DrawingBrush entry (its ElementStart is consumed): a PropertyCustom
        /// Brush value over the per-file GeometryDrawing.Brush row plus either
        /// the Geometry PropertyComplexStart holding one GeometryGroup or one
        /// RectangleGeometry, or a PropertyCustom path-data Geometry value over
        /// the per-file GeometryDrawing.Geometry row, closed by ElementEnd.
        /// The brush travels as the text-form custom value (serializer 744
        /// with a 0x02 marker plus the color text, converting through the
        /// ColorConverter like the scalar entry path); the path-data Geometry
        /// travels as the compact binary value (serializer 746, decoded by
        /// ParsePathDataGeometry) the staged SortByCategory brush carries;
        /// PresentationOptions Freeze markers land the frozen flag.
        /// Packed-ARGB and richer brushes plus richer geometries are precise
        /// follow-ups.</summary>
        private PathDrawing ParseGeometryDrawing(int offset, string display)
        {
            Color? brush = null;
            List<Rect>? group = null;
            Rect? single = null;
            List<PathFigureData>? path = null;
            var frozen = false;
            var haveGeometry = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!brush.HasValue)
                        return Fail<PathDrawing>(offset, $"GeometryDrawing in drawing entry {display} has no Brush (geometry drawings take Brush plus Geometry; style-surface follow-up)");
                    if (!haveGeometry)
                        return Fail<PathDrawing>(offset, $"GeometryDrawing in drawing entry {display} has no Geometry (geometry drawings take Brush plus Geometry; style-surface follow-up)");
                    return new PathDrawing { Brush = brush.Value, Group = group ?? new List<Rect>(), Single = single, Path = path, Frozen = frozen };
                }
                if (record == 0x06)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var serializer = ReadU16();
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in drawing entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownGeometryDrawing && name == "Brush")
                    {
                        if (brush.HasValue)
                            Fail<object>(recordOffset, $"duplicate Brush on the GeometryDrawing in drawing entry {display}");
                        if (serializer != BrushValueSerializer)
                            Fail<object>(recordOffset, $"brush custom value with serializer {serializer} in drawing entry {display} (drawing brushes take the brush-serializer text form; style-surface follow-up)");
                        var marker = ReadByte();
                        if (marker != 0x02)
                            Fail<object>(recordOffset, $"brush custom value marked 0x{marker:X2} in drawing entry {display} (drawing brushes take the 0x02 text-form solid color; style-surface follow-up)");
                        var brushText = ReadAnsiString();
                        ExpectEnd(end, recordOffset, "PropertyCustom");
                        if (brushText.Length == 0)
                            Fail<object>(recordOffset, $"brush custom value with no color text in drawing entry {display} (drawing brushes take a color text; style-surface follow-up)");
                        brush = (Color)ParseColorEntry(recordOffset, display, brushText);
                        continue;
                    }
                    if (owner == KnownGeometryDrawing && name == "Geometry")
                    {
                        if (haveGeometry)
                            Fail<object>(recordOffset, $"duplicate Geometry on the GeometryDrawing in drawing entry {display}");
                        if (serializer != PathDataSerializer)
                            Fail<object>(recordOffset, $"path-data value with serializer {serializer} in drawing entry {display} (geometry drawings take the path-data serializer form; style-surface follow-up)");
                        path = ParsePathDataGeometry(recordOffset, display, end);
                        haveGeometry = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' in drawing entry {display} (geometry drawings take the Brush custom value; style-surface follow-up)");
                }
                if (record == 0x07)
                {
                    if (haveGeometry)
                        Fail<object>(recordOffset, $"duplicate Geometry on the GeometryDrawing in drawing entry {display}");
                    var complexAttr = ReadI16();
                    if (complexAttr < 0 || complexAttr >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {complexAttr} in drawing entry {display} has no AttributeInfo record");
                    var (complexOwner, complexName) = _attrs[complexAttr];
                    if (complexOwner != KnownGeometryDrawing || complexName != "Geometry")
                        Fail<object>(recordOffset, $"attribute '{complexName}' in drawing entry {display} (geometry drawings take the Geometry complex; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"Geometry child holds record 0x{childRecord:X2} in drawing entry {display} (the Geometry complex takes one GeometryGroup or RectangleGeometry; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType == KnownGeometryGroup)
                        group = ParseGeometryGroupRects(childOffset, display);
                    else if (childType == KnownRectangleGeometry)
                        single = ParseRectangleGeometryRect(childOffset, display);
                    else
                        Fail<object>(childOffset, $"element {WellKnownName(childType)} in drawing entry {display} (the Geometry complex takes one GeometryGroup or RectangleGeometry; style-surface follow-up)");
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"Geometry complex in drawing entry {display} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    haveGeometry = true;
                    continue;
                }
                if (record == 0x24)
                    Fail<object>(recordOffset, $"converted value on the GeometryDrawing in drawing entry {display} (geometry drawings take a custom Brush plus the Geometry complex; style-surface follow-up)");
                if (record == 0x34)
                {
                    // PresentationOptions Freeze marker (every staged
                    // SortByCategory drawing freezes): a no-op unless the
                    // value is True, mirroring the scalar-entry Freeze path.
                    if (ReadPresentationOptions(recordOffset) == "True") frozen = true;
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the GeometryDrawing in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the GeometryDrawing in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the GeometryDrawing in drawing entry {display} (drawing markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"GeometryDrawing in drawing entry {display} holds record 0x{record:X2} (geometry drawings take a custom Brush plus the Geometry complex; style-surface follow-up)");
            }
        }

        /// <summary>Parses one GeometryGroup element inside a keyed
        /// DrawingBrush entry (its ElementStart is consumed): the Children
        /// content member plus RectangleGeometry children carrying Rect
        /// PropertyWithConverter values, closed by ElementEnd.</summary>
        private List<Rect> ParseGeometryGroupRects(int offset, string display)
        {
            var rects = new List<Rect>();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveContent)
                        Fail<object>(offset, $"keyed entry {display} holds a GeometryGroup with no Children (geometry groups take the Children content plus RectangleGeometry children; style-surface follow-up)");
                    return rects;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownGeometryChildren)
                        Fail<object>(recordOffset, $"content member {member} on drawing entry {display} (geometry groups take the Children content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate Children content on drawing entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveContent)
                        Fail<object>(recordOffset, $"RectangleGeometry precedes the Children content on drawing entry {display} (BAML emits Children first; style-surface follow-up)");
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownRectangleGeometry)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} inside drawing entry {display} (geometry groups take RectangleGeometry children; style-surface follow-up)");
                    rects.Add(ParseRectangleGeometryRect(recordOffset, display));
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the GeometryGroup in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the GeometryGroup in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the GeometryGroup in drawing entry {display} (drawing markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"GeometryGroup in drawing entry {display} holds record 0x{record:X2} (geometry groups take the Children content plus RectangleGeometry children; style-surface follow-up)");
            }
        }

        /// <summary>Parses one RectangleGeometry element inside a keyed
        /// DrawingBrush entry (its ElementStart is consumed): the Rect
        /// PropertyWithConverter value over the per-file RectangleGeometry
        /// row, closed by ElementEnd. The value converts through the
        /// RectConverter like the Viewport.</summary>
        private Rect ParseRectangleGeometryRect(int offset, string display)
        {
            Rect? rect = null;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!rect.HasValue)
                        return Fail<Rect>(offset, $"RectangleGeometry in drawing entry {display} has no Rect (rectangle geometries take Rect; style-surface follow-up)");
                    return rect.Value;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in drawing entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownRectangleGeometry && name == "Rect")
                    {
                        if (rect.HasValue)
                            Fail<object>(recordOffset, $"duplicate Rect on the RectangleGeometry in drawing entry {display}");
                        if (conv != ConvRect)
                            Fail<object>(recordOffset, $"Rect value with converter {conv} in drawing entry {display} (rectangle geometries take the RectConverter; style-surface follow-up)");
                        try { rect = RectangleGeometry.ParseRect(text); }
                        catch (FormatException ex)
                        {
                            Fail<object>(recordOffset, $"'{text}' is not a Rect in drawing entry {display} ({ex.Message})");
                        }
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' in drawing entry {display} (rectangle geometries take Rect; style-surface follow-up)");
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the RectangleGeometry in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the RectangleGeometry in drawing entry {display} (drawing static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the RectangleGeometry in drawing entry {display} (drawing markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"RectangleGeometry in drawing entry {display} holds record 0x{record:X2} (rectangle geometries take Rect; style-surface follow-up)");
            }
        }

        /// <summary>Reads one compact path-data Geometry value (the sized
        /// PropertyCustom payload after the Geometry row plus the 746
        /// serializer id): figure starts (0xF0 all-integral, 0x70 with a
        /// double), line points (0xD1 all-integral, 0x51 with a double) and
        /// the single trailing 19 08 close pair, validated opcode by opcode.
        /// Coordinates travel as Int32 values scaled by 1e6 or 0x05 double
        /// fallbacks. Anything else fails precise so a half-built drawing
        /// never lands.</summary>
        private List<PathFigureData> ParsePathDataGeometry(int offset, string display, int end)
        {
            var figures = new List<PathFigureData>();
            PathFigureData? current = null;
            var closed = false;
            while (_pos < end)
            {
                var opOffset = _pos;
                var op = ReadByte();
                if (op == 0xF0 || op == 0x70)
                {
                    // Figure start: two coordinates (each an Int32 scaled
                    // by 1e6 or a 0x05 double fallback). The staged brushes
                    // move with all-integral points (F0) or a double Y
                    // (0x70); richer starts stay a precise follow-up.
                    var x = ReadPathDataCoordinate(opOffset, display, end);
                    var y = ReadPathDataCoordinate(opOffset, display, end);
                    current = new PathFigureData { Start = new Point(x, y) };
                    figures.Add(current);
                    continue;
                }
                if (op == 0xD1 || op == 0x51)
                {
                    // Line point: two coordinates like the figure start
                    // (D1 all-integral, 0x51 with a double fallback).
                    var figure = current ?? Fail<PathFigureData>(opOffset, $"path-data line with no figure in drawing entry {display} (path data opens a figure before its points; style-surface follow-up)");
                    var x = ReadPathDataCoordinate(opOffset, display, end);
                    var y = ReadPathDataCoordinate(opOffset, display, end);
                    figure.Points.Add(new Point(x, y));
                    continue;
                }
                if (op == 0x19)
                {
                    // Close pair: 0x19 closes every open figure (the staged
                    // multi-figure brushes carry a single trailing close)
                    // and 0x08 ends the value with no trailing bytes. A
                    // second close always leaves trailing bytes, so the
                    // trailing gate below reports it precisely.
                    if (_pos >= end)
                        Fail<object>(opOffset, $"truncated path-data close in drawing entry {display} (path data closes with the 19 08 pair; style-surface follow-up)");
                    if (ReadByte() != 0x08)
                        Fail<object>(opOffset, $"path-data close with record 0x{bytes[_pos - 1]:X2} in drawing entry {display} (path data closes with the 19 08 pair; style-surface follow-up)");
                    if (_pos != end)
                        Fail<object>(opOffset, $"path-data value with {end - _pos} trailing bytes after the close in drawing entry {display} (path data ends at the close; style-surface follow-up)");
                    foreach (var figure in figures) figure.Closed = true;
                    closed = true;
                    continue;
                }
                Fail<object>(opOffset, $"path-data opcode 0x{op:X2} in drawing entry {display} (path data takes figure starts, line points and the close pair; style-surface follow-up)");
            }
            if (!closed)
                Fail<object>(offset, $"path-data value with no close in drawing entry {display} (path data closes with the 19 08 pair; style-surface follow-up)");
            if (figures.Count == 0)
                Fail<object>(offset, $"path-data value with no figure in drawing entry {display} (path data takes at least one figure; style-surface follow-up)");
            return figures;
        }

        /// <summary>Reads one path-data coordinate (an Int32 scaled by 1e6
        /// or a 0x05 double fallback): integral points travel as Int32
        /// values scaled by 1e6 (multiplied back with the same inexact 1e-6
        /// WPF uses, verified bit-exact against WPF's own decoder) while
        /// non-integral values like -0.0002 travel as doubles. Anything else
        /// fails precise.</summary>
        private double ReadPathDataCoordinate(int offset, string display, int end)
        {
            if (_pos >= end)
                Fail<object>(offset, $"truncated path-data coordinate in drawing entry {display} (coordinates take an Int32 or a double; style-surface follow-up)");
            if (bytes[_pos] == 0x05)
            {
                _pos++;
                if (_pos + 8 > end)
                    Fail<object>(offset, $"truncated path-data double in drawing entry {display} (doubles take 8 bytes; style-surface follow-up)");
                var value = BitConverter.ToDouble(bytes, _pos);
                _pos += 8;
                return value;
            }
            if (_pos + 4 > end)
                Fail<object>(offset, $"truncated path-data coordinate in drawing entry {display} (coordinates take an Int32 or a double; style-surface follow-up)");
            var scaled = ReadI32();
            return scaled * 1e-6;
        }

        /// <summary>Materializes validated path-data figures as a live
        /// engine StreamGeometry (Nonzero fill, filled closed figures, one
        /// polyline per figure), matching the WPF-decoded shape the staged
        /// brushes carry.</summary>
        private static StreamGeometry BuildPathGeometry(List<PathFigureData> figures)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using var context = geometry.Open();
            foreach (var figure in figures)
            {
                context.BeginFigure(figure.Start, isFilled: true, isClosed: figure.Closed);
                if (figure.Points.Count > 0) context.PolyLineTo(figure.Points, isStroked: true, isSmoothJoin: false);
            }
            return geometry;
        }

        /// <summary>Parses one keyed Storyboard entry (the ElementStart is
        /// consumed). Shape: the Children content member plus DoubleAnimation
        /// children carrying inline From/To values, converted Duration and
        /// TargetProperty values and an optional single-CubicEase
        /// EasingFunction, closed by ElementEnd. The Storyboard lands as a
        /// live engine Storyboard with each child attached through
        /// Storyboard.TargetProperty (the raw path string travels through
        /// PropertyPath like real WPF); non-shared keys (the staged
        /// transition run carries shared=0/sharedSet=1) replay the validated
        /// bags through a factory so every lookup materializes a fresh
        /// instance, like converted-element entries. Anything richer
        /// (StaticResource/markup-extension values, other animation types,
        /// other easing children) fails precise so a half-built resource
        /// never lands.</summary>
        private void ParseStoryboardEntry(int offset, object entryKey, string display, bool shared)
        {
            var animations = new List<(double From, double To, Duration Duration, string TargetProperty, EasingMode? Ease)>();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveContent)
                        Fail<object>(offset, $"keyed entry {display} holds a Storyboard with no Children (storyboards take the Children content plus DoubleAnimation children; style-surface follow-up)");
                    if (animations.Count == 0)
                        Fail<object>(offset, $"keyed entry {display} holds a Storyboard with no DoubleAnimation (storyboards take DoubleAnimation children; style-surface follow-up)");
                    Storyboard Build()
                    {
                        var board = new Storyboard();
                        foreach (var (from, to, duration, path, ease) in animations)
                        {
                            var animation = new DoubleAnimation(from, to, duration);
                            Storyboard.SetTargetProperty(animation, new PropertyPath(path));
                            if (ease.HasValue)
                                animation.EasingFunction = new CubicEase { EasingMode = ease.Value };
                            board.Children.Add(animation);
                        }
                        return board;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Build());
                    else AddDeferredEntry(offset, entryKey, display, Build);
                    return;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownStoryboardChildren)
                        Fail<object>(recordOffset, $"content member {member} on storyboard entry {display} (storyboards take the Children content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate Children content on storyboard entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveContent)
                        Fail<object>(recordOffset, $"animation child precedes the Children content on storyboard entry {display} (BAML emits Children first; style-surface follow-up)");
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownDoubleAnimation)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} inside storyboard entry {display} (storyboards take DoubleAnimation children; style-surface follow-up)");
                    animations.Add(ParseStoryboardDoubleAnimation(recordOffset, display));
                    continue;
                }
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a Storyboard (storyboards take the Children content plus DoubleAnimation children; style-surface follow-up)");
            }
        }

        /// <summary>Parses one DoubleAnimation child inside a keyed
        /// Storyboard entry (its ElementStart is consumed): inline From/To
        /// values, a converted Duration, a converted TargetProperty path and
        /// an optional single-CubicEase EasingFunction, closed by
        /// ElementEnd. Values convert eagerly with entry offsets, so the
        /// first failure is precise.</summary>
        private (double From, double To, Duration Duration, string TargetProperty, EasingMode? Ease) ParseStoryboardDoubleAnimation(
            int offset, string display)
        {
            double? from = null;
            double? to = null;
            Duration? duration = null;
            string? path = null;
            EasingMode? ease = null;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!from.HasValue)
                        return Fail<(double, double, Duration, string, EasingMode?)>(offset, $"DoubleAnimation in storyboard entry {display} has no From (animations take From plus To plus Duration plus TargetProperty; style-surface follow-up)");
                    if (!to.HasValue)
                        return Fail<(double, double, Duration, string, EasingMode?)>(offset, $"DoubleAnimation in storyboard entry {display} has no To (animations take From plus To plus Duration plus TargetProperty; style-surface follow-up)");
                    if (!duration.HasValue)
                        return Fail<(double, double, Duration, string, EasingMode?)>(offset, $"DoubleAnimation in storyboard entry {display} has no Duration (animations take From plus To plus Duration plus TargetProperty; style-surface follow-up)");
                    if (path is null)
                        return Fail<(double, double, Duration, string, EasingMode?)>(offset, $"DoubleAnimation in storyboard entry {display} has no TargetProperty (animations take From plus To plus Duration plus TargetProperty; style-surface follow-up)");
                    return (from.Value, to.Value, duration.Value, path, ease);
                }
                if (record == 0x05)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    ExpectEnd(end, recordOffset, "Property");
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in storyboard entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner != KnownDoubleAnimation || (name != "From" && name != "To"))
                        Fail<object>(recordOffset, $"attribute '{name}' in storyboard entry {display} (storyboards take From/To; style-surface follow-up)");
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        Fail<object>(recordOffset, $"'{text}' is not a Double for {display}");
                    if (name == "From")
                    {
                        if (from.HasValue)
                            Fail<object>(recordOffset, $"duplicate From on the DoubleAnimation in storyboard entry {display}");
                        from = number;
                    }
                    else
                    {
                        if (to.HasValue)
                            Fail<object>(recordOffset, $"duplicate To on the DoubleAnimation in storyboard entry {display}");
                        to = number;
                    }
                    continue;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {attrId} in storyboard entry {display} has no AttributeInfo record");
                    var (owner, name) = _attrs[attrId];
                    if (owner == KnownTimeline && name == "Duration")
                    {
                        if (duration.HasValue)
                            Fail<object>(recordOffset, $"duplicate Duration on the DoubleAnimation in storyboard entry {display}");
                        if (conv != ConvDuration)
                            Fail<object>(recordOffset, $"Duration value with converter {conv} in storyboard entry {display} (storyboard Durations take the Duration converter; style-surface follow-up)");
                        try { duration = Duration.Parse(text); }
                        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                        {
                            return Fail<(double, double, Duration, string, EasingMode?)>(recordOffset, $"'{text}' is not a Duration for {display} ({ex.Message})");
                        }
                        continue;
                    }
                    if (owner == KnownStoryboard && name == "TargetProperty")
                    {
                        if (path is not null)
                            Fail<object>(recordOffset, $"duplicate TargetProperty on the DoubleAnimation in storyboard entry {display}");
                        if (conv != ConvPropertyPath)
                            Fail<object>(recordOffset, $"TargetProperty value with converter {conv} in storyboard entry {display} (storyboard target paths take the PropertyPath converter; style-surface follow-up)");
                        path = text;
                        continue;
                    }
                    Fail<object>(recordOffset, $"attribute '{name}' in storyboard entry {display} (storyboards take From/To/Duration/TargetProperty/EasingFunction; style-surface follow-up)");
                    continue;
                }
                if (record == 0x07)
                {
                    if (ease.HasValue)
                        Fail<object>(recordOffset, $"duplicate EasingFunction on the DoubleAnimation in storyboard entry {display}");
                    var complexAttr = ReadI16();
                    if (complexAttr < 0 || complexAttr >= _attrs.Count)
                        Fail<object>(recordOffset, $"attribute id {complexAttr} in storyboard entry {display} has no AttributeInfo record");
                    var (complexOwner, complexName) = _attrs[complexAttr];
                    if (complexOwner != KnownDoubleAnimation || complexName != "EasingFunction")
                        Fail<object>(recordOffset, $"attribute '{complexName}' in storyboard entry {display} (storyboards take From/To/Duration/TargetProperty/EasingFunction; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"easing child holds record 0x{childRecord:X2} in storyboard entry {display} (easing children take one element; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType < 0 || childType >= _types.Count ||
                        !string.Equals(_types[childType].Name, "System.Windows.Media.Animation.CubicEase", StringComparison.Ordinal))
                        Fail<object>(childOffset, $"easing child <{TypeKeyName(childType)}> in storyboard entry {display} (only CubicEase easing lands; style-surface follow-up)");
                    ease = ReadStoryboardEasingMode(childOffset, display);
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"easing child in storyboard entry {display} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the DoubleAnimation in storyboard entry {display} (storyboard static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the DoubleAnimation in storyboard entry {display} (storyboard static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the DoubleAnimation in storyboard entry {display} (storyboard markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"DoubleAnimation in storyboard entry {display} holds record 0x{record:X2} (animations take From plus To plus Duration plus TargetProperty plus EasingFunction; style-surface follow-up)");
            }
        }

        /// <summary>Reads the EasingMode value inside a staged CubicEase
        /// easing child (its ElementStart is consumed): one converted
        /// EasingMode value over the EasingFunctionBase AttributeInfo row,
        /// closed by ElementEnd. Only EaseIn/EaseOut/EaseInOut land.</summary>
        private EasingMode ReadStoryboardEasingMode(int offset, string display)
        {
            var record = ReadEntryRecord(out var recordOffset);
            if (record != 0x24)
                Fail<object>(recordOffset, $"easing child in storyboard entry {display} holds record 0x{record:X2} (easing values take EasingMode; style-surface follow-up)");
            var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
            if (attrId < 0 || attrId >= _attrs.Count)
                Fail<object>(recordOffset, $"attribute id {attrId} in storyboard entry {display} has no AttributeInfo record");
            var (owner, name) = _attrs[attrId];
            if (name != "EasingMode" || owner < 0 || owner >= _types.Count ||
                !string.Equals(_types[owner].Name, "System.Windows.Media.Animation.EasingFunctionBase", StringComparison.Ordinal))
                Fail<object>(recordOffset, $"attribute '{name}' in storyboard entry {display} (easing values take EasingMode over EasingFunctionBase; style-surface follow-up)");
            if (conv != ConvEnum)
                Fail<object>(recordOffset, $"EasingMode value with converter {conv} in storyboard entry {display} (easing values take the Enum converter; style-surface follow-up)");
            EasingMode mode;
            try { mode = (EasingMode)Enum.Parse(typeof(EasingMode), text, ignoreCase: false); }
            catch (ArgumentException)
            {
                return Fail<EasingMode>(recordOffset, $"easing value '{text}' does not convert to EasingMode in storyboard entry {display} (easing values take EaseIn/EaseOut/EaseInOut)");
            }
            var endRecord = ReadEntryRecord(out var endOffset);
            if (endRecord != 0x04)
                Fail<object>(endOffset, $"easing child in storyboard entry {display} holds record 0x{endRecord:X2} (easing values take one EasingMode; style-surface follow-up)");
            _ = offset;
            return mode;
        }

        /// <summary>Reads one significant entry record, consuming line
        /// markers transparently (they carry positions only).</summary>
        private byte ReadEntryRecord(out int recordOffset)
        {
            while (true)
            {
                recordOffset = _pos;
                var record = ReadByte();
                if (record == 0x35) { _line = (int)ReadU32(); _col = (int)ReadU32(); continue; }
                if (record == 0x36) { _col = (int)ReadU32(); continue; }
                return record;
            }
        }

        private void ParseCustomElementEntry(int offset, object entryKey, string display, short typeId, bool shared)
        {
            // Per-file CLR type entries (converters, providers, behaviors):
            // an empty element instantiates the type; anything with content
            // is a converted value (simple PropertyWithConverter properties
            // plus PropertyComplexStart children around one empty framework
            // child each, the staged FluidMoveBehavior shape). Values convert
            // eagerly with entry offsets, so the first failure is precise;
            // non-shared keys (x:Shared="False") replay the validated bags
            // through a factory so every lookup materializes a fresh instance.
            if (typeId < 0 || typeId >= _types.Count)
                Fail<object>(offset, $"keyed entry {display} names unknown type id {typeId} (only {_types.Count} TypeInfo records)");
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x04)
            {
                var empty = InstantiateEntryType(offset, display, typeId);
                if (shared) AddEntry(offset, entryKey, display, empty);
                else AddDeferredEntry(offset, entryKey, display, () => InstantiateEntryType(offset, display, typeId));
                return;
            }
            var simples = new List<(System.Reflection.PropertyInfo Property, object? Value)>();
            var complexes = new List<(System.Reflection.PropertyInfo Property, object Child)>();
            var pending = record;
            var pendingOffset = recordOffset;
            while (true)
            {
                if (pending == 0x04)
                {
                    object Materialize()
                    {
                        var value = InstantiateEntryType(offset, display, typeId);
                        foreach (var (property, simple) in simples) property.SetValue(value, simple);
                        foreach (var (property, child) in complexes) property.SetValue(value, child);
                        return value;
                    }
                    if (shared) AddEntry(offset, entryKey, display, Materialize());
                    else AddDeferredEntry(offset, entryKey, display, Materialize);
                    return;
                }
                if (pending == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(pendingOffset);
                    simples.Add(ResolveConvertedProperty(pendingOffset, display, typeId, attrId, text, conv));
                    pending = ReadEntryRecord(out pendingOffset);
                    continue;
                }
                if (pending == 0x07)
                {
                    complexes.Add(ReadConvertedComplexChild(pendingOffset, display, typeId));
                    pending = ReadEntryRecord(out pendingOffset);
                    continue;
                }
                if (pending == 0x19)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds a DefAttribute (x:Name/uid wiring is a namescope follow-up)");
                if (pending == 0x2E)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds a ContentProperty (converted content properties are a follow-up)");
                if (pending == 0x22)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds a PropertyTypeReference (converted type references are a follow-up)");
                if (pending == 0x38)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds a StaticResource value (converted static values are a follow-up)");
                if (pending == 0x37)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds an optimized StaticResource value (converted static values are a follow-up)");
                if (pending == 0x23)
                    Fail<object>(pendingOffset, $"keyed entry {display} holds a markup-extension value (converted markup values are a follow-up)");
                Fail<object>(pendingOffset, $"keyed entry {display} holds <{TypeInfoName(typeId)}> with record 0x{pending:X2} (converted-element values are a follow-up)");
            }
        }

        /// <summary>Reads a PropertyWithConverter payload inside a converted
        /// entry (sized I16 attribute + text + I16 converter).</summary>
        private (short AttrId, string Text, short Converter) ReadConvertedPropertyPayload(int offset)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "PropertyWithConverter");
            return (attrId, text, conv);
        }

        /// <summary>Resolves one converted simple property to a validated
        /// (PropertyInfo, value) bag: the attribute must name a settable CLR
        /// property on the entry type and the text must convert through the
        /// record's converter for the property type.</summary>
        private (System.Reflection.PropertyInfo Property, object? Value) ResolveConvertedProperty(
            int recordOffset, string display, short typeId, short attrId, string text, short conv)
        {
            if (attrId < 0 || attrId >= _attrs.Count)
            {
                Fail<object>(recordOffset, $"converted property id {attrId} in entry {display} has no AttributeInfo record (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var (owner, name) = _attrs[attrId];
            var fullName = TypeInfoName(typeId);
            var entryType = ResolveEntryType(recordOffset, display, typeId).Type;
            var property = entryType.GetProperty(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                Fail<object>(recordOffset, $"converted property '{name}' (owner {owner}) on <{fullName}> in entry {display} is not a settable CLR property (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var target = property.PropertyType;
            object? converted = null;
            var ok = false;
            if (target == typeof(string) && conv == ConvString) { converted = text; ok = true; }
            else if (target == typeof(bool) && conv == ConvBool) { ok = bool.TryParse(text, out var b); converted = b; }
            else if (target == typeof(double) && conv == ConvDouble && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) { converted = d; ok = true; }
            else if (target.IsEnum && conv == ConvEnum)
            {
                try { converted = Enum.Parse(target, text, ignoreCase: false); ok = true; }
                catch (ArgumentException) { ok = false; }
            }
            else if (target == typeof(Duration) && conv == ConvDuration)
            {
                try { converted = Duration.Parse(text); ok = true; }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                { ok = false; }
            }
            if (!ok)
            {
                Fail<object>(recordOffset, $"converted property '{name}' with value '{text}' and converter {conv} does not convert to {target.Name} in entry {display} (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            return (property, converted);
        }

        /// <summary>Reads one PropertyComplexStart child inside a converted
        /// entry (fixed record + U16 attribute, byte-verified on the staged
        /// Behavior entries) holding a single empty child element, and
        /// resolves it to a validated (PropertyInfo, child) bag. Only the
        /// staged PowerEase shape instantiates; anything richer fails
        /// precise so a half-built value never lands.</summary>
        private (System.Reflection.PropertyInfo Property, object Child) ReadConvertedComplexChild(
            int offset, string display, short typeId)
        {
            if (_pos + 2 > bytes.Length)
            {
                Fail<object>(offset, $"truncated BAML complex property in entry {display}");
                throw new InvalidOperationException("unreachable");
            }
            var attrId = ReadI16();
            if (attrId < 0 || attrId >= _attrs.Count)
            {
                Fail<object>(offset, $"converted complex property id {attrId} in entry {display} has no AttributeInfo record (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var (owner, name) = _attrs[attrId];
            var fullName = TypeInfoName(typeId);
            var childRecord = ReadEntryRecord(out var childOffset);
            if (childRecord != 0x03)
            {
                Fail<object>(childOffset, $"converted complex property '{name}' in entry {display} holds record 0x{childRecord:X2} (converted complex children take one element; converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            if (_pos + 3 > bytes.Length)
            {
                Fail<object>(childOffset, $"truncated BAML complex child in entry {display}");
                throw new InvalidOperationException("unreachable");
            }
            var childType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            var endRecord = ReadEntryRecord(out var endOffset);
            if (endRecord != 0x04)
            {
                Fail<object>(endOffset, $"converted complex child of '{name}' in entry {display} holds record 0x{endRecord:X2} (converted complex children are empty elements; converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
            {
                Fail<object>(closeOffset, $"converted complex child of '{name}' in entry {display} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            object child;
            if (childType >= 0 && childType < _types.Count &&
                string.Equals(_types[childType].Name, "System.Windows.Media.Animation.PowerEase", StringComparison.Ordinal))
                child = new PowerEase();
            else
            {
                Fail<object>(childOffset, $"converted complex child <{TypeKeyName(childType)}> in entry {display} (only empty PowerEase children land; converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var entryType = ResolveEntryType(offset, display, typeId).Type;
            var property = entryType.GetProperty(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            if (property is null || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                Fail<object>(offset, $"converted complex property '{name}' (owner {owner}) on <{fullName}> in entry {display} is not a settable CLR property (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            if (!property.PropertyType.IsInstanceOfType(child))
            {
                Fail<object>(offset, $"converted complex property '{name}' takes {property.PropertyType.Name} in entry {display} (converted-element values are a follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            return (property, child);
        }

        private void ParseEmptyElementEntry(int offset, object entryKey, string display, short typeId)
        {
            var record = ReadEntryRecord(out var recordOffset);
            if (record != 0x04)
                Fail<object>(recordOffset, $"keyed entry {display} holds {WellKnownName(typeId)} with record 0x{record:X2} (expected an empty element)");
            AddEntry(offset, entryKey, display, InstantiateEntryType(offset, display, typeId));
        }

        private void ParseScalarEntry(int offset, object entryKey, string display, short typeId)
        {
            // Scalar text entries: ElementStart + TextWithConverter +
            // ElementEnd, with PresentationOptions Freeze markers and line
            // markers transparent (the real theme.baml freezes its Geometry
            // entries). Anything else is a precise boundary.
            string? text = null;
            short converter = 0;
            var freeze = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") freeze = true;
                    continue;
                }
                if (record == 0x11)
                {
                    if (text is not null)
                        Fail<object>(recordOffset, $"duplicate text on dictionary entry {display}");
                    (text, converter) = ReadTextWithConverterPayload(recordOffset);
                    continue;
                }
                if (record == 0x04)
                {
                    if (text is null)
                        Fail<object>(offset, $"dictionary entry {display} has no value (keyed entries take a single TextWithConverter)");
                    var value = ConvertScalar(offset, display, typeId, converter, text!);
                    if (freeze && value is Freezable freezable) freezable.Freeze();
                    AddEntry(offset, entryKey, display, value);
                    return;
                }
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} (scalar entries take a single TextWithConverter; style-surface follow-up)");
            }
        }

        /// <summary>Parses one keyed PathGeometry entry (the ElementStart
        /// is consumed): a single PropertyWithConverter carrying the
        /// Figures text (well-known member -89 through the
        /// PathFigureCollectionConverter -445, byte-verified on the staged
        /// keyidx-162 'TreeArrow' entry and a genuine net10.0-windows
        /// fixture compiling the upstream PathGeometry/Figures block),
        /// with PresentationOptions Freeze markers and line markers
        /// transparent like the scalar entries, closed by ElementEnd. The
        /// text parses through Geometry.Parse like the scalar Geometry
        /// entries (the 366-char staged text lands as one PathGeometry);
        /// the entry lands shared like the scalar entries (the staged key
        /// is shared). Anything else fails precise so a half-built
        /// geometry never lands.</summary>
        private void ParsePathGeometryEntry(int offset, object entryKey, string display, bool shared)
        {
            _ = shared;
            string? text = null;
            var freeze = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x34)
                {
                    if (ReadPresentationOptions(recordOffset) == "True") freeze = true;
                    continue;
                }
                if (record == 0x24)
                {
                    if (text is not null)
                        Fail<object>(recordOffset, $"duplicate Figures on dictionary entry {display}");
                    text = ReadPathFiguresPayload(recordOffset, display);
                    continue;
                }
                if (record == 0x04)
                {
                    if (text is null)
                        Fail<object>(offset, $"dictionary entry {display} has no value (keyed PathGeometry entries take a single Figures PropertyWithConverter)");
                    var geometry = ParsePathFigures(offset, display, text!);
                    if (freeze && geometry is Freezable freezable) freezable.Freeze();
                    AddEntry(offset, entryKey, display, geometry);
                    return;
                }
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} (PathGeometry entries take a single Figures PropertyWithConverter; style-surface follow-up)");
            }
        }

        /// <summary>Reads the Figures PropertyWithConverter payload of a
        /// keyed PathGeometry entry (sized I16 attribute + text + I16
        /// converter): the attribute must be the well-known Figures member
        /// and the converter the PathFigureCollectionConverter, both
        /// byte-verified on the staged entry and the genuine fixture.</summary>
        private string ReadPathFiguresPayload(int offset, string display)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "PropertyWithConverter");
            if (attrId != KnownFiguresMember)
                Fail<object>(offset, $"PathGeometry entry {display} sets {(attrId < 0 ? WellKnownMemberName(attrId) : $"attribute id {attrId}")} (PathGeometry entries take the Figures member; style-surface follow-up)");
            if (conv != ConvPathFigures)
                Fail<object>(offset, $"Figures on PathGeometry entry {display} uses converter {conv} (PathGeometry entries take the PathFigureCollectionConverter; style-surface follow-up)");
            return text;
        }

        /// <summary>Parses Figures path data into a live engine
        /// PathGeometry through Geometry.Parse (the staged text parses to
        /// one PathGeometry); garbage text fails precise like the scalar
        /// Geometry entries.</summary>
        private PathGeometry ParsePathFigures(int offset, string display, string text)
        {
            Geometry parsed;
            try
            {
                parsed = Geometry.Parse(text);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return Fail<PathGeometry>(offset, $"'{text}' is not a PathGeometry for {display} ({ex.Message})");
            }
            if (parsed is not PathGeometry path)
                return Fail<PathGeometry>(offset, $"'{text}' parsed to {parsed.GetType().Name} for {display} (PathGeometry entries take path data)");
            return path;
        }

        /// <summary>Parses one keyed Style entry (the ElementStart is consumed).
        /// Shape: PropertyTypeReference for TargetType, ContentProperty for
        /// Style.Setters, Setter elements with PropertyCustom Property plus a
        /// PropertyWithConverter Value or a complex ControlTemplate value,
        /// closed by the entry ElementEnd, plus an optional Trigger list
        /// (Trigger/DataTrigger elements with PropertyWithConverter or
        /// inline values and Setter children). The style lands as a live
        /// engine Style with its Triggers; anything richer (BasedOn,
        /// StaticResource/markup-extension values, other complex values,
        /// other trigger forms) fails precise so a half-built style never
        /// lands.</summary>
        /// <summary>Parses one keyed Style entry (the ElementStart is
        /// consumed): an optional BasedOn StaticResource (fixed
        /// PropertyWithStaticResourceId over the per-file Style.BasedOn row),
        /// the TargetType PropertyTypeReference, the Setters content member,
        /// Setter children plus an optional Triggers list, closed by
        /// ElementEnd. The staged SearchPathStyle run is this exact shape
        /// (BasedOn plus one Data StaticResource setter); the base key and
        /// the static setter values resolve at apply time through the engine
        /// Style/Setter machinery like the XAML loader text path. Non-shared
        /// keys are a precise follow-up. Anything richer fails precise so a
        /// half-built style never lands.</summary>
        private void ParseStyleEntry(int offset, object entryKey, string display, bool shared)
        {
            Type? targetType = null;
            string? basedOnKey = null;
            var setters = new List<Setter>();
            var triggers = new List<TriggerBase>();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"keyed entry {display} holds a Style with no TargetType (style-surface follow-up)");
                    if (!shared)
                        Fail<object>(offset, $"keyed entry {display} is non-shared (keyed styles take shared entries; style-surface follow-up)");
                    var style = new Style(targetType!) { BasedOnKey = basedOnKey };
                    foreach (var setter in setters) style.Setters.Add(setter);
                    foreach (var trigger in triggers) style.Triggers.Add(trigger);
                    AddEntry(offset, entryKey, display, style);
                    return;
                }
                if (record == 0x38)
                {
                    if (basedOnKey is not null)
                        Fail<object>(recordOffset, $"duplicate BasedOn on style entry {display}");
                    basedOnKey = ReadStyleBasedOnKey(recordOffset, display);
                    continue;
                }
                if (record == 0x22)
                {
                    if (targetType is not null)
                        Fail<object>(recordOffset, $"duplicate TargetType on style entry {display}");
                    targetType = ReadStyleTargetType(recordOffset, display);
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownStyleSetters)
                        Fail<object>(recordOffset, $"content member {member} on style entry {display} (styles take the Setters content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    var elementType = ReadI16();
                    _pos += 1; // flags byte
                    if (elementType != KnownSetter)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} inside style entry {display} (styles take Setter elements; style-surface follow-up)");
                    setters.Add(ParseStyleSetter(recordOffset, display, targetType));
                    continue;
                }
                if (record == 0x0B)
                {
                    if (targetType is null)
                        Fail<object>(recordOffset, $"trigger list in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    ParseTriggerList(recordOffset, offset, display, targetType!, triggers, KnownStyle);
                    continue;
                }
                Fail<object>(recordOffset, $"keyed entry {display} holds record 0x{record:X2} in a Style (styles take TargetType plus Setter elements; style-surface follow-up)");
            }
        }

        /// <summary>Reads the BasedOn StaticResource of a keyed Style entry
        /// (fixed PropertyWithStaticResourceId: the per-file Style.BasedOn
        /// attribute plus the key-block static id, byte-verified on the staged
        /// SearchPathStyle entry plus a genuine net10.0-windows fixture
        /// compiling the upstream Themes/Basic/Paths.xaml block, both emitting
        /// the fixed 5-byte shape with the OptimizedStaticResource declaration
        /// riding the key). Returns the base-style key name for apply-time
        /// resolution through the engine Style.BasedOnKey path, like the XAML
        /// loader text path.</summary>
        private string ReadStyleBasedOnKey(int offset, string display)
        {
            if (_pos + 4 > bytes.Length)
                Fail<object>(offset, $"truncated BAML StaticResource value in keyed entry {display}");
            var basedOnAttr = ReadI16();
            var staticId = (int)ReadU16();
            RequireAttribute(offset, display, basedOnAttr, KnownStyle, "BasedOn");
            if (_entryStatics is null || staticId < 0 || staticId >= _entryStatics.Count)
                Fail<object>(offset, $"StaticResource id {staticId} in style entry {display} has no StaticResource index (only {(_entryStatics is null ? 0 : _entryStatics.Count)} indexed static values; style-surface follow-up)");
            var (flags, id, key) = _entryStatics![staticId];
            _ = id;
            if (flags != 0 || string.IsNullOrEmpty(key))
                Fail<object>(offset, $"StaticResource id {staticId} in style entry {display} names a non-string static resource (style static values take string-keyed resources; style-surface follow-up)");
            return key;
        }

        /// <summary>Parses one Style.Triggers list (the 0x0B header is
        /// consumed): Trigger and DataTrigger elements in document order
        /// through the closing 0x0C. Anything else fails precise so a
        /// half-built trigger never lands.</summary>
        private void ParseTriggerList(int listOffset, int offset, string display, Type targetType, List<TriggerBase> triggers, short triggersOwner)
        {
            _ = offset;
            // PropertyIListStart is a bare record: 0x0B followed by the U16
            // AttributeInfo id of the list property (verified framing: the
            // staged style lists carry the Style.Triggers row, the staged
            // ToggleBlock template list the ControlTemplate.Triggers row).
            var attrId = (int)ReadU16();
            RequireAttribute(listOffset, display, (short)attrId, triggersOwner, "Triggers");
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x0C) return;
                if (record != 0x03)
                    Fail<object>(recordOffset, $"trigger list in style entry {display} holds record 0x{record:X2} (trigger lists take Trigger/DataTrigger elements; style-surface follow-up)");
                var elementType = ReadI16();
                _pos += 1; // flags byte (skipped like ElementStart)
                if (elementType == KnownTrigger)
                    triggers.Add(ParseBamlTrigger(recordOffset, display, targetType));
                else if (elementType == KnownDataTrigger)
                    triggers.Add(ParseBamlDataTrigger(recordOffset, display, targetType));
                else
                    Fail<object>(recordOffset, $"element {WellKnownName(elementType)} in the trigger list in style entry {display} (trigger lists take Trigger/DataTrigger; style-surface follow-up)");
            }
        }

        /// <summary>Parses one Trigger element (its ElementStart is
        /// consumed): a qualified or short-form PropertyCustom Property, a
        /// PropertyWithConverter Value kept raw like XAML DataTrigger values
        /// (evaluation normalizes bool/double/int/enum text) or a complex
        /// x:Null Value landing null (the staged ToggleBlock IsChecked
        /// trigger takes it, emitted before the Property like the genuine
        /// fixture, so either order parses), the Setters content member and
        /// Setter children, closed by ElementEnd. The staged SnapRect
        /// triggers are this exact shape (TipElement.Placement qualified
        /// conditions with enum values); StaticResource (0x38),
        /// optimized-static (0x37) and markup-extension (0x23) values are
        /// precise follow-ups.</summary>
        private Trigger ParseBamlTrigger(int offset, string display, Type targetType)
        {
            var property = string.Empty;
            var haveProperty = false;
            object? value = null;
            var haveValue = false;
            var setters = new List<Setter>();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveProperty)
                        return Fail<Trigger>(offset, $"trigger in style entry {display} has no Property (triggers take Property plus Value plus Setters; style-surface follow-up)");
                    if (!haveValue)
                        return Fail<Trigger>(offset, $"trigger '{property}' in style entry {display} has no Value (triggers take Property plus Value plus Setters; style-surface follow-up)");
                    var trigger = new Trigger { Property = property, Value = value };
                    foreach (var setter in setters) trigger.Setters.Add(setter);
                    return trigger;
                }
                if (record == 0x06)
                {
                    if (haveProperty)
                        Fail<object>(recordOffset, $"duplicate Property on the trigger in style entry {display}");
                    property = ReadTriggerProperty(recordOffset, display, targetType);
                    haveProperty = true;
                    continue;
                }
                if (record == 0x24)
                {
                    if (!haveProperty)
                        Fail<object>(recordOffset, $"Value before Property on the trigger in style entry {display} (BAML emits Property first)");
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on the trigger '{property}' in style entry {display}");
                    value = ReadTriggerConditionValue(recordOffset, display);
                    haveValue = true;
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownTriggerSetters)
                        Fail<object>(recordOffset, $"content member {member} on the trigger '{property}' in style entry {display} (triggers take the Setters content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate content on the trigger '{property}' in style entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownSetter)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} on the trigger '{property}' in style entry {display} (triggers take Setter elements; style-surface follow-up)");
                    setters.Add(ParseStyleSetter(recordOffset, display, targetType));
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on the trigger '{property}' in style entry {display}");
                    value = ReadNullTriggerValue(recordOffset, display);
                    haveValue = true;
                    continue;
                }
                if (record == 0x05)
                    Fail<object>(recordOffset, $"inline value on the trigger '{property}' in style entry {display} (trigger conditions take PropertyWithConverter values; DataTrigger values take the inline form; style-surface follow-up)");
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the trigger '{property}' in style entry {display} (trigger static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the trigger '{property}' in style entry {display} (trigger static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the trigger '{property}' in style entry {display} (trigger markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"trigger '{property}' in style entry {display} holds record 0x{record:X2} (triggers take Property plus Value plus Setters; style-surface follow-up)");
            }
        }

        /// <summary>Reads a trigger PropertyCustom reference (sized I16
        /// attribute + U16 serializer + payload): the attribute must be the
        /// Trigger.Property row, qualified owners resolve through the same
        /// owner table as setter properties (well-known framework owners
        /// plus converted full names, returning the owner-qualified
        /// "Owner.Name" form like set_Property(DependencyProperty)) and
        /// short-form members resolve against the style TargetType.
        /// Returns the evaluation key, never the DP itself.</summary>
        private string ReadTriggerProperty(int offset, string display, Type targetType)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var serializer = ReadU16();
            RequireAttribute(offset, display, attrId, KnownTrigger, "Property");
            if (serializer == SerDependencyProperty)
            {
                var memberId = ReadI16();
                ExpectEnd(end, offset, "PropertyCustom");
                var shortName = ShortMemberDisplay(memberId);
                if (shortName is null)
                    Fail<object>(offset, $"trigger property {WellKnownMemberName(memberId)} in style entry {display} (short-form trigger properties beyond {ShortMemberList()}; style-surface follow-up)");
                if (DependencyProperty.FromName(targetType, shortName) is null)
                    Fail<object>(offset, $"'{shortName}' is not a dependency property of {targetType.Name} (trigger in style entry {display})");
                return shortName!;
            }
            if (serializer == SerDependencyPropertyQualified)
            {
                var ownerId = ReadI16();
                var propertyName = ReadAnsiString();
                ExpectEnd(end, offset, "PropertyCustom");
                var owner = ResolvePropertyOwner(offset, display, ownerId);
                var dp = DependencyProperty.FromName(owner, propertyName);
                if (dp is null)
                    Fail<object>(offset, $"'{propertyName}' is not a dependency property of {owner.Name} (trigger in style entry {display})");
                return owner.Name + "." + propertyName;
            }
            Fail<object>(offset, $"trigger in style entry {display} uses PropertyCustom serializer {serializer} (style-surface follow-up)");
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>Reads a trigger PropertyWithConverter condition value
        /// (sized I16 attribute + text + I16 converter): the attribute must
        /// be the Trigger.Value row. The text is kept raw like XAML
        /// DataTrigger values (evaluation normalizes bool/double/int/enum
        /// text); the converter id is consumed but not validated, since
        /// conversion is type-driven at evaluation.</summary>
        private string ReadTriggerConditionValue(int offset, string display)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            _ = ReadI16(); // converter id: conversion is type-driven at evaluation, like setter values
            ExpectEnd(end, offset, "PropertyWithConverter");
            RequireAttribute(offset, display, attrId, KnownTrigger, "Value");
            return text;
        }

        /// <summary>Reads a complex x:Null trigger value
        /// (PropertyComplexStart holding one NullExtension element, closed
        /// by PropertyComplexEnd): the attribute must be the Trigger.Value
        /// row and the child the NullExtension element (well-known id -422,
        /// byte-verified against a genuine net10 fixture compiling
        /// Value="{x:Null}"). The value lands as null, matching the live
        /// upstream ToggleBlock IsChecked trigger.</summary>
        private object? ReadNullTriggerValue(int offset, string display)
        {
            var attrId = ReadI16();
            RequireAttribute(offset, display, attrId, KnownTrigger, "Value");
            var childRecord = ReadEntryRecord(out var childOffset);
            if (childRecord != 0x03)
                Fail<object>(childOffset, $"null value on the trigger in style entry {display} holds record 0x{childRecord:X2} (null values take one NullExtension element; style-surface follow-up)");
            var childType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            if (childType != KnownNullExtension)
                Fail<object>(childOffset, $"null value on the trigger in style entry {display} holds <{TargetTypeName(childType)}> (null values take a NullExtension element; style-surface follow-up)");
            var endRecord = ReadEntryRecord(out var endOffset);
            if (endRecord != 0x04)
                Fail<object>(endOffset, $"null value on the trigger in style entry {display} holds record 0x{endRecord:X2} (null elements close with ElementEnd; style-surface follow-up)");
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
                Fail<object>(closeOffset, $"null value on the trigger in style entry {display} holds record 0x{closeRecord:X2} (complex values close with PropertyComplexEnd; style-surface follow-up)");
            return null;
        }

        /// <summary>Parses one DataTrigger element (its ElementStart is
        /// consumed): one complex Binding child (constructor-path only), one
        /// inline Value kept raw, the Setters content member and Setter
        /// children, closed by ElementEnd. The staged SnapRect/TargetArea data
        /// triggers are this exact shape (IsSelecting/IsDrawing bindings
        /// with "True" plus Visibility/BorderThickness setters); richer
        /// bindings (RelativeSource, ElementName, converters, simples) are
        /// precise follow-ups.</summary>
        private DataTrigger ParseBamlDataTrigger(int offset, string display, Type targetType)
        {
            Binding? binding = null;
            var haveBinding = false;
            string? value = null;
            var haveValue = false;
            var setters = new List<Setter>();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveBinding)
                        return Fail<DataTrigger>(offset, $"data trigger in style entry {display} has no Binding (data triggers take Binding plus Value plus Setters; style-surface follow-up)");
                    if (!haveValue)
                        return Fail<DataTrigger>(offset, $"data trigger in style entry {display} has no Value (data triggers take Binding plus Value plus Setters; style-surface follow-up)");
                    var trigger = new DataTrigger { Binding = binding, Value = value };
                    foreach (var setter in setters) trigger.Setters.Add(setter);
                    return trigger;
                }
                if (record == 0x07)
                {
                    if (haveBinding)
                        Fail<object>(recordOffset, $"duplicate Binding on the data trigger in style entry {display}");
                    binding = ReadTriggerBinding(recordOffset, display);
                    haveBinding = true;
                    continue;
                }
                if (record == 0x05)
                {
                    if (!haveBinding)
                        Fail<object>(recordOffset, $"Value before Binding on the data trigger in style entry {display} (BAML emits Binding first)");
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on the data trigger in style entry {display}");
                    value = ReadInlineTriggerValue(recordOffset, display);
                    haveValue = true;
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownDataTriggerSetters)
                        Fail<object>(recordOffset, $"content member {member} on the data trigger in style entry {display} (data triggers take the Setters content; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate content on the data trigger in style entry {display}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    var elementType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (elementType != KnownSetter)
                        Fail<object>(recordOffset, $"element {WellKnownName(elementType)} on the data trigger in style entry {display} (data triggers take Setter elements; style-surface follow-up)");
                    setters.Add(ParseStyleSetter(recordOffset, display, targetType));
                    continue;
                }
                if (record == 0x38)
                    Fail<object>(recordOffset, $"StaticResource value on the data trigger in style entry {display} (trigger static values are a style-surface follow-up)");
                if (record == 0x37)
                    Fail<object>(recordOffset, $"optimized StaticResource value on the data trigger in style entry {display} (trigger static values are a style-surface follow-up)");
                if (record == 0x23)
                    Fail<object>(recordOffset, $"markup-extension value on the data trigger in style entry {display} (trigger markup values are a style-surface follow-up)");
                Fail<object>(recordOffset, $"data trigger in style entry {display} holds record 0x{record:X2} (data triggers take Binding plus Value plus Setters; style-surface follow-up)");
            }
        }

        /// <summary>Reads one complex Binding child on a DataTrigger (fixed
        /// U16 attribute, no size prefix): the attribute must be the
        /// DataTrigger.Binding row. The single child element must be a
        /// Binding with an optional single-text constructor path and no
        /// other content, closed by PropertyComplexEnd. Anything richer
        /// fails precise so a half-built binding never lands.</summary>
        private Binding ReadTriggerBinding(int offset, string display)
        {
            if (_pos + 2 > bytes.Length)
                Fail<object>(offset, $"truncated BAML binding on the data trigger in style entry {display}");
            var attrId = ReadI16();
            RequireAttribute(offset, display, attrId, KnownDataTrigger, "Binding");
            var childRecord = ReadEntryRecord(out var childOffset);
            if (childRecord != 0x03)
                Fail<object>(childOffset, $"binding on the data trigger in style entry {display} holds record 0x{childRecord:X2} (bindings take one element; style-surface follow-up)");
            var childType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            if (childType != KnownBinding)
                Fail<object>(childOffset, $"binding on the data trigger in style entry {display} holds <{TargetTypeName(childType)}> (bindings take a Binding element; style-surface follow-up)");
            var binding = ParseBamlTriggerBinding(childOffset, display);
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
                Fail<object>(closeOffset, $"binding on the data trigger in style entry {display} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
            return binding;
        }

        /// <summary>Parses a Binding element on a DataTrigger (its
        /// ElementStart is consumed): an optional single-text constructor
        /// group carries the Path, closed by ElementEnd. Complex children
        /// (RelativeSource/ElementName), simples and multi-argument paths
        /// are precise follow-ups.</summary>
        private Binding ParseBamlTriggerBinding(int offset, string display)
        {
            _ = offset;
            var path = string.Empty;
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the data-trigger Binding");
                if (args.Count > 1)
                    Fail<object>(recordOffset, $"data-trigger Binding in style entry {display} takes one constructor argument (the path; richer markup-extension arguments are a style-surface follow-up)");
                if (args.Count == 1) path = args[0];
                record = ReadEntryRecord(out recordOffset);
            }
            var binding = new Binding(path);
            while (true)
            {
                if (record == 0x04) return binding;
                if (record == 0x07)
                    Fail<object>(recordOffset, $"data-trigger Binding in style entry {display} holds a complex property (bindings take a path; RelativeSource/ElementName are a style-surface follow-up)");
                if (record == 0x24)
                    Fail<object>(recordOffset, $"data-trigger Binding in style entry {display} holds a simple value (bindings take a path; style-surface follow-up)");
                Fail<object>(recordOffset, $"data-trigger Binding in style entry {display} holds record 0x{record:X2} (bindings take a path; style-surface follow-up)");
            }
        }

        /// <summary>Reads an inline trigger value (sized I16 attribute +
        /// 7-bit-len text, no converter id): the attribute must be the
        /// DataTrigger.Value row. The text is kept raw like XAML
        /// DataTrigger values. Verified framing: a real net10 fixture
        /// compiling Value="True" emits exactly this shape, byte-identical
        /// to the staged SnapRect/TargetArea values modulo the attribute index.</summary>
        private string ReadInlineTriggerValue(int offset, string display)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            ExpectEnd(end, offset, "Property");
            RequireAttribute(offset, display, attrId, KnownDataTrigger, "Value");
            return text;
        }

        /// <summary>Short-form member display names shared by setter and
        /// trigger properties (resolution walks the style TargetType).</summary>
        private static string? ShortMemberDisplay(short memberId) => memberId switch
        {
            KnownFlowDirection => "FlowDirection",
            KnownTemplateMember => "Template",
            KnownFocusableMember => "Focusable",
            KnownIsEnabledMember => "IsEnabled",
            KnownHAlignMember => "HorizontalAlignment",
            KnownVAlignMember => "VerticalAlignment",
            KnownHContentMember => "HorizontalContentAlignment",
            KnownVContentMember => "VerticalContentAlignment",
            KnownBackgroundMember => "Background",
            KnownHeightMember => "Height",
            KnownWidthMember => "Width",
            KnownFillMember => "Fill",
            KnownVisibilityMember => "Visibility",
            KnownForegroundSourceMember => "Foreground",
            KnownBorderThicknessMember => "BorderThickness",
            KnownBorderBrushMember => "BorderBrush",
            KnownBorderBackgroundMember => "Background",
            KnownMarginAttribute => "Margin",
            KnownIsTabStopMember => "IsTabStop",
            KnownPaddingMember => "Padding",
            KnownItemsPanelMember => "ItemsPanel",
            KnownRenderTransformMember => "RenderTransform",
            KnownImageSourceMember => "Source",
            _ => null,
        };

        /// <summary>Short-form member list for precise errors.</summary>
        private static string ShortMemberList() =>
            "FlowDirection/Template/Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/HorizontalContentAlignment/VerticalContentAlignment/Background/Height/Width/Fill/Visibility/Foreground/BorderThickness/BorderBrush/Margin/IsTabStop/Padding/ItemsPanel/RenderTransform/Source";

        /// <summary>Reads a PropertyTypeReference (fixed 4 bytes: I16
        /// attribute + I16 type, no size prefix) as a Style TargetType. The
        /// attribute must be the Style.TargetType AttributeInfo row
        /// (validated through the per-file table, never a hardcoded index).
        /// </summary>
        private Type ReadStyleTargetType(int offset, string display)
        {
            var attrId = ReadI16();
            var typeId = ReadI16();
            RequireAttribute(offset, display, attrId, KnownStyle, "TargetType");
            return ResolveTargetTypeId(offset, display, typeId);
        }

        /// <summary>Resolves a style/template TargetType id: the verified
        /// well-known framework types (Path/Border/ContentControl/GridSplitter/
        /// Rectangle/RepeatButton/GroupBox/Thumb/ToolBarTray/ComboBox/MenuItem/TextBlock/ListBoxItem) plus per-file
        /// CLR types (engine short names and converted full names, never instantiated
        /// here). ComboBox routes to the engine Selector lineage (shim, never ported):
        /// the staged DataGridComboBoxColumnStyle entry targets it, and the Focusable
        /// plus Template short-form setters land through the hierarchy walk like WPF. TextBlock
/// routes to the engine text element: the staged DataGridTextColumnStyle entry targets
/// it, and the short-form TextTrimming setter lands through TextBlock.TextTrimmingProperty like WPF. ListBoxItem routes to the engine item container (shim, never ported): the staged ListBoxItemCustom entry lands its nine setters through the Control hierarchy walk like WPF.</summary>
        private Type ResolveTargetTypeId(int offset, string display, short typeId)
        {
            if (typeId == KnownPath) return typeof(Path);
            if (typeId == KnownBorder) return typeof(Border);
            if (typeId == KnownContentControl) return typeof(ContentControl);
            if (typeId == KnownGridSplitter) return typeof(GridSplitter);
            if (typeId == KnownRectangle) return typeof(Rectangle);
            if (typeId == KnownRepeatButton) return typeof(RepeatButton);
            if (typeId == KnownGroupBox) return typeof(GroupBox);
            if (typeId == KnownThumb) return typeof(Thumb);
            if (typeId == KnownToolBarTray) return typeof(ToolBarTray);
            if (typeId == KnownComboBox) return typeof(ComboBox);
            if (typeId == KnownMenuItem) return typeof(MenuItem);
            if (typeId == KnownTextBlock) return typeof(TextBlock);
            if (typeId == KnownListBoxItem) return typeof(ListBoxItem);
            if (typeId >= 0) return ResolveEntryType(offset, display, typeId).Type;
            Fail<object>(offset, $"keyed entry {display} targets {TargetTypeName(typeId)} (style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb/ToolBarTray/ComboBox/MenuItem/TextBlock/ListBoxItem are a style-surface follow-up)");
            throw new InvalidOperationException("unreachable");
        }

        private string TargetTypeName(short typeId)
        {
            if (typeId >= 0) return $"<{TypeInfoName(typeId)}>";
            return WellKnownName(typeId);
        }

        /// <summary>Parses one Setter element (its ElementStart is consumed):
        /// a PropertyCustom Property plus a PropertyWithConverter Value, a
        /// PropertyCustom packed-ARGB brush Value or a PropertyComplexStart
        /// ControlTemplate value, closed by ElementEnd, with an optional
        /// TargetName string (the staged ToggleBlock template-trigger
        /// setters name their ContentPresenter parts) landing on the Setter
        /// like the XAML loader path. StaticResource (0x38) values land deferred
        /// as "{StaticResource key}" RawValues like the XAML loader text path;
        /// markup-extension (0x23) values are precise follow-ups.
        /// Short-form Properties resolve against the style TargetType like
        /// WPF (real files emit TargetType first).</summary>
        private Setter ParseStyleSetter(int offset, string display, Type? targetType)
        {
            DependencyProperty? property = null;
            var propertyDisplay = string.Empty;
            object? value = null;
            var haveValue = false;
            string? targetName = null;
            string? staticValueKey = null;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (property is null)
                        Fail<object>(offset, $"setter in style entry {display} has no Property (setters take PropertyCustom plus a value)");
                    if (!haveValue)
                        Fail<object>(offset, $"setter '{propertyDisplay}' in style entry {display} has no Value (setters take a simple converted value; style-surface follow-up)");
                    if (staticValueKey is not null)
                        return new Setter(property!, null, targetName) { RawValue = "{StaticResource " + staticValueKey + "}" };
                    return new Setter(property!, value, targetName);
                }
                if (record == 0x06)
                {
                    if (property is null)
                    {
                        (property, propertyDisplay) = ReadSetterProperty(recordOffset, display, targetType);
                        continue;
                    }
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on setter '{propertyDisplay}' in style entry {display}");
                    (value, haveValue) = (ReadBrushSetterValue(recordOffset, display, property!, propertyDisplay), true);
                    continue;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (IsTargetNameAttribute(attrId))
                    {
                        if (conv != ConvString)
                            Fail<object>(recordOffset, $"TargetName '{text}' on setter '{propertyDisplay}' in style entry {display} uses converter {conv} (target names take StringConverter; style-surface follow-up)");
                        if (targetName is not null)
                            Fail<object>(recordOffset, $"duplicate TargetName on setter '{propertyDisplay}' in style entry {display} (setters take one TargetName; style-surface follow-up)");
                        targetName = text;
                        continue;
                    }
                    if (property is null)
                        Fail<object>(recordOffset, $"Value before Property on a setter in style entry {display} (BAML emits Property first)");
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on setter '{propertyDisplay}' in style entry {display}");
                    (value, haveValue) = (ConvertSetterText(recordOffset, display, property!, propertyDisplay, attrId, text), true);
                    continue;
                }
                if (record == 0x38)
                {
                    if (property is null)
                        Fail<object>(recordOffset, $"Value before Property on a setter in style entry {display} (BAML emits Property first)");
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on setter '{propertyDisplay}' in style entry {display}");
                    staticValueKey = ReadSetterStaticKey(recordOffset, display, propertyDisplay);
                    haveValue = true;
                    continue;
                }
                if (record == 0x23)
                    Fail<object>(recordOffset, $"setter '{propertyDisplay}' in style entry {display} holds a markup-extension value (style-surface follow-up)");
                if (record == 0x07)
                {
                    if (property is null)
                        Fail<object>(recordOffset, $"Value before Property on a setter in style entry {display} (BAML emits Property first)");
                    if (haveValue)
                        Fail<object>(recordOffset, $"duplicate Value on setter '{propertyDisplay}' in style entry {display}");
                    (value, haveValue) = (ParseComplexSetterValue(recordOffset, display, property!, propertyDisplay), true);
                    continue;
                }
                Fail<object>(recordOffset, $"setter '{propertyDisplay}' in style entry {display} holds record 0x{record:X2} (setters take PropertyCustom plus a simple value; style-surface follow-up)");
            }
        }

        /// <summary>Reads a StaticResource setter value (fixed
        /// PropertyWithStaticResourceId: the per-file Setter.Value attribute
        /// plus the key-block static id, byte-verified on the staged
        /// SearchPathStyle Data setter plus a genuine net10.0-windows fixture
        /// compiling the upstream Path style block, both emitting the fixed
        /// 5-byte shape). Returns the resource key name; the caller lands it
        /// as a deferred "{StaticResource key}" RawValue like the XAML loader
        /// text path, resolving at apply time through the engine setter
        /// machinery (missing keys fail loud, genuine mismatches fail loud).</summary>
        private string ReadSetterStaticKey(int offset, string display, string setterDisplay)
        {
            if (_pos + 4 > bytes.Length)
                Fail<object>(offset, $"truncated BAML StaticResource value on setter '{setterDisplay}' in style entry {display}");
            var valueAttr = ReadI16();
            var staticId = (int)ReadU16();
            RequireAttribute(offset, display, valueAttr, KnownSetter, "Value");
            if (_entryStatics is null || staticId < 0 || staticId >= _entryStatics.Count)
                Fail<object>(offset, $"StaticResource id {staticId} on setter '{setterDisplay}' in style entry {display} has no StaticResource index (only {(_entryStatics is null ? 0 : _entryStatics.Count)} indexed static values; style-surface follow-up)");
            var (flags, id, key) = _entryStatics![staticId];
            _ = id;
            if (flags != 0 || string.IsNullOrEmpty(key))
                Fail<object>(offset, $"StaticResource id {staticId} on setter '{setterDisplay}' in style entry {display} names a non-string static resource (setter static values take string-keyed resources; style-surface follow-up)");
            return key;
        }

        /// <summary>Reads a PropertyWithConverter value (sized I16 attribute
        /// + text + I16 converter) for a resolved setter property. The
        /// attribute must be the Setter.Value row; the text converts through
        /// the DP type like real-WPF load conversion (enums, bools,
        /// Thickness and friends ride StyleValueConverter).</summary>
        private object ReadSetterValue(int offset, string entryDisplay, DependencyProperty property, string display)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var text = ReadAnsiString();
            _ = ReadI16(); // converter id: conversion is driven by the DP type
            ExpectEnd(end, offset, "PropertyWithConverter");
            return ConvertSetterText(offset, entryDisplay, property, display, attrId, text);
        }

        /// <summary>Converts an already-framed PropertyWithConverter setter
        /// text through the resolved DP type (the attribute must be the
        /// Setter.Value row; TargetName rows branch before reaching here).
        /// </summary>
        private object ConvertSetterText(int offset, string entryDisplay, DependencyProperty property, string display, short attrId, string text)
        {
            RequireAttribute(offset, entryDisplay, attrId, KnownSetter, "Value");
            try { return StyleValueConverter.Convert(text, property.PropertyType); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException or ArgumentException)
            {
                return Fail<object>(offset, $"'{text}' is not a {property.PropertyType.Name} for setter '{display}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
        }

        /// <summary>True for the TargetName string attribute on a
        /// template-trigger Setter (per-file AttributeInfo row owned by
        /// Setter, byte-verified against the staged ToggleBlock setters and
        /// a genuine net10 fixture compiling TargetName setters).</summary>
        private bool IsTargetNameAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownSetter && string.Equals(name, "TargetName", StringComparison.Ordinal);
        }

        /// <summary>Reads a PropertyCustom brush value (sized I16 attribute
        /// + U16 serializer + bytes) for a resolved setter property. The
        /// attribute must be the Setter.Value row and the serializer the
        /// packed-ARGB brush serializer (744); the 5 payload bytes are 0x01
        /// plus the LE ARGB quad, landing a SolidColorBrush like the XAML
        /// text path. Anything else fails precise so a half-built setter
        /// never lands. The staged Notification Background (Transparent) and
        /// 'SnapRect4ScreenshotStyle' Fill (DodgerBlue) values are this exact shape.
        /// </summary>
        private object ReadBrushSetterValue(int offset, string entryDisplay, DependencyProperty property, string display)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var serializer = ReadU16();
            RequireAttribute(offset, entryDisplay, attrId, KnownSetter, "Value");
            if (serializer != BrushValueSerializer)
                Fail<object>(offset, $"setter '{display}' in style entry {entryDisplay} holds a PropertyCustom value with serializer {serializer} (setter values take converted text, packed-ARGB brushes or templates; style-surface follow-up)");
            if (property.PropertyType != typeof(Brush))
                Fail<object>(offset, $"setter '{display}' in style entry {entryDisplay} holds a packed-ARGB brush for {property.PropertyType.Name} (brush values take a Brush property; style-surface follow-up)");
            if (end - _pos != 5)
                Fail<object>(offset, $"setter '{display}' in style entry {entryDisplay} holds a {end - _pos}-byte brush value (packed-ARGB brushes take 0x01 plus 4 bytes; style-surface follow-up)");
            var mark = ReadByte();
            var blue = ReadByte();
            var green = ReadByte();
            var red = ReadByte();
            var alpha = ReadByte();
            ExpectEnd(end, offset, "PropertyCustom");
            if (mark != 0x01)
                Fail<object>(offset, $"setter '{display}' in style entry {entryDisplay} holds a brush value marked 0x{mark:X2} (packed-ARGB brushes start with 0x01; style-surface follow-up)");
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }

        /// <summary>Reads a PropertyComplexStart setter value (fixed U16
        /// Setter.Value attribute, no size prefix) holding one element,
        /// closed by PropertyComplexEnd. ControlTemplate values land (the staged
        /// BlendEffectBox Template setter is this exact shape) and Binding values
        /// land (the staged Gripper Height TemplatedParent transfer) and NullExtension values
        /// land as null (the staged ListBoxItemCustom FocusVisualStyle) and ItemsPanelTemplate
        /// values land (the staged RibbonTab ItemsPanel setter holding the StackPanel
        /// Orientation-Horizontal visual) and TransformGroup values land (the staged RightPathStyle
/// RenderTransform setter holding the four-child Scale/Skew/Rotate/Translate group); anything else fails precise so a half-built
        /// setter never lands.
        /// </summary>
        private object? ParseComplexSetterValue(int offset, string entryDisplay, DependencyProperty property, string display)
        {
            _ = property;
            if (_pos + 2 > bytes.Length)
                Fail<object>(offset, $"truncated BAML complex value on setter '{display}' in style entry {entryDisplay}");
            var attrId = ReadI16();
            RequireAttribute(offset, entryDisplay, attrId, KnownSetter, "Value");
            var elementRecord = ReadEntryRecord(out var elementOffset);
            if (elementRecord != 0x03)
                Fail<object>(elementOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{elementRecord:X2} in a complex value (complex setter values take one element; style-surface follow-up)");
            var templateType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            if (templateType == KnownBinding)
            {
                var binding = ParseStyleSetterBinding(elementOffset, entryDisplay, display);
                var bindingClose = ReadEntryRecord(out var bindingCloseOffset);
                if (bindingClose != 0x08)
                    Fail<object>(bindingCloseOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{bindingClose:X2} in a complex value (complex values close with PropertyComplexEnd; style-surface follow-up)");
                return binding;
            }
            if (templateType == KnownNullExtension)
            {
                var nullEnd = ReadEntryRecord(out var nullEndOffset);
                if (nullEnd != 0x04)
                    Fail<object>(nullEndOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{nullEnd:X2} in a null value (null elements close with ElementEnd; style-surface follow-up)");
                var nullClose = ReadEntryRecord(out var nullCloseOffset);
                if (nullClose != 0x08)
                    Fail<object>(nullCloseOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{nullClose:X2} in a null value (complex values close with PropertyComplexEnd; style-surface follow-up)");
                return null;
            }
            if (templateType == KnownItemsPanelTemplate
                && string.Equals(property.Name, "ItemsPanel", StringComparison.Ordinal))
            {
                var panel = ParseItemsPanelTemplate(elementOffset, entryDisplay, display);
                var panelClose = ReadEntryRecord(out var panelCloseOffset);
                if (panelClose != 0x08)
                    Fail<object>(panelCloseOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{panelClose:X2} in an items-panel value (complex values close with PropertyComplexEnd; style-surface follow-up)");
                return new ItemsPanelTemplate(panel);
            }
            if (templateType == KnownTransformGroup
                && string.Equals(property.Name, "RenderTransform", StringComparison.Ordinal))
            {
                var transforms = ParseStyleTransformGroup(elementOffset, entryDisplay, display);
                var transformsClose = ReadEntryRecord(out var transformsCloseOffset);
                if (transformsClose != 0x08)
                    Fail<object>(transformsCloseOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{transformsClose:X2} in a transform-group value (complex values close with PropertyComplexEnd; style-surface follow-up)");
                return transforms;
            }
            if (templateType != KnownControlTemplate)
                Fail<object>(elementOffset, $"setter '{display}' in style entry {entryDisplay} holds <{TargetTypeName(templateType)}> in a complex value (complex setter values take a ControlTemplate, a Binding, an ItemsPanelTemplate, a NullExtension or a TransformGroup; style-surface follow-up)");
            var template = ParseControlTemplate(elementOffset, entryDisplay, display);
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
                Fail<object>(closeOffset, $"setter '{display}' in style entry {entryDisplay} holds record 0x{closeRecord:X2} in a complex value (complex values close with PropertyComplexEnd; style-surface follow-up)");
            return template;
        }

        /// <summary>Parses a Binding value on a style Setter (its ElementStart
        /// is consumed): an optional single-text constructor group carries the
        /// Path (the staged Gripper Height binds "ActualHeight"), an optional
        /// RelativeSource complex child carries the mode (the staged value
        /// binds TemplatedParent), closed by ElementEnd with the complex
        /// closed by PropertyComplexEnd in the caller. Converter/
        /// ConverterParameter/Path simples, ElementName and multi-argument
        /// paths are precise follow-ups. The transfer lands as the Setter
        /// value like the XAML loader path, activating per-element on apply.
        /// </summary>
        private Binding ParseStyleSetterBinding(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var path = string.Empty;
            var binding = new Binding(path);
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the style-setter Binding");
                if (args.Count > 1)
                    Fail<object>(recordOffset, $"setter Binding on setter '{setterDisplay}' in style entry {entryDisplay} takes one constructor argument (the path; richer markup-extension arguments are a style-surface follow-up)");
                if (args.Count == 1) path = args[0];
                record = ReadEntryRecord(out recordOffset);
            }
            while (true)
            {
                if (record == 0x04)
                {
                    binding.Path = new PropertyPath(path);
                    return binding;
                }
                if (record == 0x07)
                {
                    if (_pos + 2 > bytes.Length)
                        Fail<object>(recordOffset, $"truncated BAML Binding property on setter '{setterDisplay}' in style entry {entryDisplay}");
                    var attrId = ReadI16();
                    if (!IsRelativeSourceAttribute(attrId))
                        Fail<object>(recordOffset, $"setter Binding holds complex property {BindingAttributeDisplay(attrId)} on setter '{setterDisplay}' in style entry {entryDisplay} (setter bindings take RelativeSource; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"setter Binding holds record 0x{childRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (RelativeSource takes one element; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownRelativeSource)
                        Fail<object>(childOffset, $"setter Binding holds <{TargetTypeName(childType)}> on setter '{setterDisplay}' in style entry {entryDisplay} (RelativeSource takes a RelativeSource child; style-surface follow-up)");
                    if (binding.RelativeSource is not null)
                        Fail<object>(childOffset, $"duplicate RelativeSource on the setter Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    binding.RelativeSource = ParseStyleRelativeSource(childOffset, entryDisplay, setterDisplay);
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"setter Binding holds record 0x{closeRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                if (record == 0x24)
                    Fail<object>(recordOffset, $"setter Binding on setter '{setterDisplay}' in style entry {entryDisplay} holds a simple value (setter bindings take the path plus RelativeSource; style-surface follow-up)");
                if (record == 0x38)
                    Fail<object>(recordOffset, $"setter Binding on setter '{setterDisplay}' in style entry {entryDisplay} holds a StaticResource value (setter bindings take the path plus RelativeSource; style-surface follow-up)");
                Fail<object>(recordOffset, $"setter Binding on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{record:X2} (setter bindings take the path plus RelativeSource; style-surface follow-up)");
            }
        }

        /// <summary>Parses a RelativeSource element inside a style-setter
        /// Binding (its ElementStart is consumed): an optional
        /// constructor-parameters group carries the mode name (the staged
        /// Gripper Height binds TemplatedParent) plus an AncestorType Property
        /// (the staged ListBoxItemCustom horizontal alignment binds the
        /// ItemsControl ancestor). Closed by ElementEnd.
        /// </summary>
        private RelativeSource ParseStyleRelativeSource(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var mode = RelativeSourceMode.FindAncestor;
            Type? ancestorType = null;
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the style-setter RelativeSource");
                if (args.Count != 1)
                    Fail<object>(recordOffset, $"style-setter RelativeSource on setter '{setterDisplay}' in style entry {entryDisplay} takes one constructor argument (the mode; richer markup-extension arguments are a style-surface follow-up)");
                try { mode = Enum.Parse<RelativeSourceMode>(args[0], ignoreCase: false); }
                catch (ArgumentException)
                {
                    return Fail<RelativeSource>(recordOffset, $"style-setter RelativeSource names '{args[0]}' on setter '{setterDisplay}' in style entry {entryDisplay} (relative-source modes take FindAncestor/Self/TemplatedParent/PreviousData; style-surface follow-up)");
                }
                record = ReadEntryRecord(out recordOffset);
            }
            while (true)
            {
                if (record == 0x04)
                {
                    var source = new RelativeSource(mode);
                    if (ancestorType is not null) source.AncestorType = ancestorType;
                    return source;
                }
                if (record == 0x05)
                {
                    var end = StartSized(out _);
                    var ancestorAttr = ReadI16();
                    var ancestorText = ReadAnsiString();
                    ExpectEnd(end, recordOffset, "Property");
                    if (!IsRelativeSourceAncestorTypeAttribute(ancestorAttr))
                        Fail<object>(recordOffset, $"style-setter RelativeSource holds attribute {BindingAttributeDisplay(ancestorAttr)} on setter '{setterDisplay}' in style entry {entryDisplay} (relative sources take a mode plus AncestorType; style-surface follow-up)");
                    if (ancestorType is not null)
                        Fail<object>(recordOffset, $"duplicate AncestorType on the style-setter RelativeSource on setter '{setterDisplay}' in style entry {entryDisplay}");
                    ancestorType = ResolveAncestorType(recordOffset, entryDisplay, setterDisplay, ancestorText);
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                Fail<object>(recordOffset, $"style-setter RelativeSource holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (relative sources take a mode plus AncestorType; style-surface follow-up)");
            }
        }

        /// <summary>Parses a ControlTemplate element (its ElementStart is
        /// consumed): a PropertyTypeReference TargetType, the VisualTree
        /// content member, one visual child and an optional Triggers list,
        /// closed by ElementEnd. The staged BlendEffectBox template holds a
        /// single ContentPresenter with ContentSource="ActualContent", the
        /// FlipClock template a StackPanel, the SimpleItemsControl template
        /// a Border with TemplateBindings and a named StackPanel child, the
        /// staged RepeatButton templates a Border with a packed-ARGB
        /// Background plus Height/Width TemplateBindings and a Rectangle
        /// child (or a bare ContentPresenter/Rectangle visual), the staged
        /// ToggleBlock template a Border with a converted SimplePanel child
        /// plus three named ContentPresenters plus three IsChecked Triggers
        /// with TargetName setters and an x:Null value, the staged
        /// 'GroupBoxBaseTopTemplate' keyed entry a Grid with RowDefinitions
        /// plus two Borders carrying TemplateBinding and TemplatedParent-bound
        /// values and the staged 'GroupBoxBaseLeftTemplate' keyed entry a
        /// Grid with ColumnDefinitions plus two Borders carrying TemplateBinding
        /// and TemplatedParent-bound values plus a LayoutTransform chain;
        /// anything richer fails precise. The staged
        /// 'CircleProgressBarTemplate' keyed entry roots a converted
        /// SimplePanel with Arc/TextBlock children. Names join the template
        /// namescope for the TargetName setters. The template retains its
        /// visual root without inflation markup, so applying it throws the
        /// markup-less precise boundary until BAML-tree inflation lands
        /// (never a shared instance).</summary>
        private ControlTemplate ParseControlTemplate(int offset, string entryDisplay, string setterDisplay)
        {
            Type? targetType = null;
            UIElement? visual = null;
            var bindings = new List<(object Element, string Property)>();
            var names = new Dictionary<string, object>(StringComparer.Ordinal);
            var triggers = new List<TriggerBase>();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"template on setter '{setterDisplay}' in style entry {entryDisplay} has no TargetType (templates take TargetType plus one visual plus Triggers; style-surface follow-up)");
                    if (visual is null)
                        Fail<object>(offset, $"template on setter '{setterDisplay}' in style entry {entryDisplay} has no visual (templates take TargetType plus one visual plus Triggers; style-surface follow-up)");
                    var template = new ControlTemplate { TargetType = targetType, VisualTree = visual, TemplateBindings = bindings, TemplateNames = names };
                    foreach (var trigger in triggers) template.Triggers.Add(trigger);
                    return template;
                }
                if (record == 0x22)
                {
                    if (targetType is not null)
                        Fail<object>(recordOffset, $"duplicate TargetType on the template on setter '{setterDisplay}' in style entry {entryDisplay}");
                    var attrId = ReadI16();
                    var typeId = ReadI16();
                    RequireAttribute(recordOffset, entryDisplay, attrId, KnownControlTemplate, "TargetType");
                    targetType = ResolveTargetTypeId(recordOffset, entryDisplay, typeId);
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownVisualTree)
                        Fail<object>(recordOffset, $"content member {member} on the template on setter '{setterDisplay}' in style entry {entryDisplay} (templates take the VisualTree content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    if (visual is not null)
                        Fail<object>(recordOffset, $"second visual on the template on setter '{setterDisplay}' in style entry {entryDisplay} (templates take one visual; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    visual = ParseTemplateRootVisual(childType, recordOffset, entryDisplay, setterDisplay, bindings, names);
                    continue;
                }
                if (record == 0x0B)
                {
                    if (targetType is null)
                        Fail<object>(recordOffset, $"trigger list on the template on setter '{setterDisplay}' in style entry {entryDisplay} precedes its template TargetType (BAML emits TargetType first; style-surface follow-up)");
                    ParseTriggerList(recordOffset, offset, entryDisplay, targetType!, triggers, KnownControlTemplate);
                    continue;
                }
                Fail<object>(recordOffset, $"template on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{record:X2} (templates take TargetType plus one visual plus Triggers; style-surface follow-up)");
            }
        }

        /// <summary>Parses one template root visual (its ElementStart type id
        /// and flags are consumed): ContentPresenter, StackPanel, Border,
        /// Rectangle, Grid, TextBlock or a per-file converted Panel (the staged
        /// GroupBox template roots its Grid with RowDefinitions plus Borders
        /// carrying TemplateBinding and TemplatedParent-bound values; the
        /// staged CircleProgressBar template roots its converted SimplePanel
        /// with Arc/TextBlock children; the staged ComboBoxColumnStyle template
        /// roots a bare TextBlock carrying IsHitTestVisible plus a Text TemplateBinding
        /// plus TextTrimming). Anything else fails precise so a
        private UIElement ParseTemplateRootVisual(short childType, int recordOffset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names)
        {
            if (childType == KnownContentPresenter)
                return ParseTemplateContentPresenter(recordOffset, entryDisplay, setterDisplay, bindings, names);
            if (childType == KnownStackPanel)
                return ParseTemplateStackPanel(recordOffset, entryDisplay, setterDisplay);
            if (childType == KnownBorder)
                return ParseTemplateBorder(recordOffset, entryDisplay, setterDisplay, bindings, names);
            if (childType == KnownRectangle)
                return ParseTemplateRectangle(recordOffset, entryDisplay, setterDisplay, bindings);
            if (childType == KnownGrid)
                return ParseTemplateGrid(recordOffset, entryDisplay, setterDisplay, bindings, names);
            if (childType == KnownTextBlock)
                return ParseTemplateTextBlock(recordOffset, entryDisplay, setterDisplay, bindings, names);
            if (childType >= 0)
                return ParseTemplateRootPanel(recordOffset, entryDisplay, setterDisplay, bindings, names, childType);
            return Fail<UIElement>(recordOffset, $"element {TargetTypeName(childType)} on the template on setter '{setterDisplay}' in style entry {entryDisplay} (templates take a ContentPresenter, StackPanel, Border, Rectangle, Grid, TextBlock or SimplePanel visual; style-surface follow-up)");
        }

        /// <summary>Parses a converted Panel template root visual (its
        /// ElementStart is consumed): resolves the CLR type like entry
        /// values (engine short names and converted full names, never
        /// fabricated), requires a Panel-derived visual, instantiates it
        /// (the staged CircleProgressBar root is the converted
        /// HandyControl.Controls.SimplePanel with a default constructor),
        /// lands the Name simple in the template namescope (the staged root
        /// carries x:Name="TemplateRoot"), hosts ContentPresenter, Border,
        /// converted-Shape and TextBlock children through the Children
        /// content member, and closes on ElementEnd. The staged
        /// 'CircleProgressBarTemplate' entry is this exact shape (two
        /// converted Arc children with ArcThickness TemplateBindings plus
        /// Stretch/EndAngle/StrokeThickness simples and a TextBlock child
        /// with alignment simples plus Foreground/Text TemplateBindings and
        /// a TemplatedParent-bound Visibility Binding carrying the
        /// Boolean2VisibilityConverter static); the staged
        /// 'BadgeDotTemplate' entry roots the same converted SimplePanel
        /// with a bare ContentPresenter plus a Border carrying Width/Height/
        /// CornerRadius simples, Margin/HorizontalAlignment/
        /// VerticalAlignment/Background/BorderBrush/BorderThickness
        /// TemplateBindings and a TemplateBinding-with-Converter Visibility
        /// transfer. Anything richer fails precise.</summary>
        private Panel ParseTemplateRootPanel(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names, short typeId)
        {
            var (elementType, _) = ResolveEntryType(offset, $"<{TypeInfoName(typeId)}> on the template on setter '{setterDisplay}' in style entry {entryDisplay}", typeId);
            if (!typeof(Panel).IsAssignableFrom(elementType))
                return Fail<Panel>(offset, $"element <{TypeInfoName(typeId)}> on the template on setter '{setterDisplay}' in style entry {entryDisplay} is not a Panel (template roots take a SimplePanel visual; style-surface follow-up)");
            Panel panel;
            try { panel = (Panel)Activator.CreateInstance(elementType, nonPublic: true)!; }
            catch (Exception ex)
            {
                return Fail<Panel>(offset, $"cannot instantiate template root element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, elementType, attrId);
                    if (property.Name == "Name" && conv == ConvString)
                    {
                        if (names.ContainsKey(text))
                            Fail<object>(recordOffset, $"duplicate template part name '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} (template names take unique names; style-surface follow-up)");
                        panel.SetValue(property, text);
                        names[text] = panel;
                        continue;
                    }
                    if (attrId == KnownVisibilityMember && conv == ConvEnum)
                    {
                        // The staged RibbonTab template roots its converted
                        // SimplePanel collapsed (Visibility Collapsed through
                        // the EnumConverter, byte-identical to a genuine
                        // net10 fixture): the value lands through the DP
                        // store like other template simples.
                        panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                        continue;
                    }
                    Fail<object>(recordOffset, $"template root panel property '{display}' with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (root panels take the Name string plus Visibility; style-surface follow-up)");
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownChildren)
                        Fail<object>(recordOffset, $"content member {member} on the template root panel on setter '{setterDisplay}' in style entry {entryDisplay} (root panels take the Children content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType == KnownTextBlock)
                        panel.Children.Add(ParseTemplateTextBlock(recordOffset, entryDisplay, setterDisplay, bindings, names));
                    else if (childType == KnownContentPresenter)
                        panel.Children.Add(ParseTemplateContentPresenter(recordOffset, entryDisplay, setterDisplay, bindings, names));
                    else if (childType == KnownBorder)
                        panel.Children.Add(ParseTemplateBorder(recordOffset, entryDisplay, setterDisplay, bindings, names));
                    else if (childType == KnownScrollViewer)
                        panel.Children.Add(ParseTemplateScrollViewer(recordOffset, entryDisplay, setterDisplay));
                    else if (childType >= 0)
                        panel.Children.Add(ParseTemplateConvertedShape(recordOffset, entryDisplay, setterDisplay, bindings, names, childType));
                    else
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} on the template root panel on setter '{setterDisplay}' in style entry {entryDisplay} (root panels take ContentPresenter, Border, ScrollViewer, converted Shape and TextBlock children; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template root panel holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (root panels take the Name string plus the Children content with ContentPresenter, Border, ScrollViewer, Shape and TextBlock children; style-surface follow-up)");
            }
        }

        /// <summary>Parses a ScrollViewer template child (its ElementStart is
        /// consumed): the HorizontalScrollBarVisibility/VerticalScrollBarVisibility
        /// enum simples land through the DP store (the staged RibbonTab
        /// ScrollViewer carries Hidden/Disabled through the EnumConverter,
        /// byte-identical to a genuine net10.0-windows fixture compiling the
        /// same ScrollViewer/ItemsPresenter block), the per-file attached
        /// Orientation PWC lands through the converted owner tables (the staged
        /// child carries hc:ScrollViewerAttach.Orientation Horizontal from the
        /// converted HandyControl.Controls.ScrollViewerAttach TypeInfo row),
        /// the Content member hosts the single ItemsPresenter child, closed by
        /// ElementEnd. The staged RibbonTab template is this exact shape.
        /// Anything richer fails precise so a half-built visual never lands.</summary>
        private ScrollViewer ParseTemplateScrollViewer(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var viewer = new ScrollViewer();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return viewer;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(ScrollViewer), attrId);
                    if (!ReferenceEquals(property, ScrollViewer.HorizontalScrollBarVisibilityProperty)
                        && !ReferenceEquals(property, ScrollViewer.VerticalScrollBarVisibilityProperty)
                        && !(attrId >= 0 && property.Name == "Orientation"))
                        Fail<object>(recordOffset, $"template ScrollViewer property '{display}' with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (scroll viewers take the Orientation attached value plus the Hidden/Disabled scrollbar policies; style-surface follow-up)");
                    viewer.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownContent)
                        Fail<object>(recordOffset, $"content member {member} on the template ScrollViewer on setter '{setterDisplay}' in style entry {entryDisplay} (scroll viewers take the Content member; style-surface follow-up)");
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate Content content on the template ScrollViewer on setter '{setterDisplay}' in style entry {entryDisplay}");
                    haveContent = true;
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownItemsPresenter)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} on the template ScrollViewer on setter '{setterDisplay}' in style entry {entryDisplay} (scroll viewers take the ItemsPresenter child; style-surface follow-up)");
                    if (!haveContent)
                        Fail<object>(recordOffset, $"ItemsPresenter precedes the Content content on the template ScrollViewer on setter '{setterDisplay}' in style entry {entryDisplay} (BAML emits Content first; style-surface follow-up)");
                    if (viewer.Content is not null)
                        Fail<object>(recordOffset, $"duplicate ItemsPresenter on the template ScrollViewer on setter '{setterDisplay}' in style entry {entryDisplay} (scroll viewers take one ItemsPresenter; style-surface follow-up)");
                    var presenterEnd = ReadEntryRecord(out var presenterOffset);
                    if (presenterEnd != 0x04)
                        Fail<object>(presenterOffset, $"template ItemsPresenter holds record 0x{presenterEnd:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (template presenters are empty; style-surface follow-up)");
                    viewer.Content = new ItemsPresenter();
                    continue;
                }
                Fail<object>(recordOffset, $"template ScrollViewer holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (scroll viewers take the Orientation attached value plus the Hidden/Disabled scrollbar policies with the Content member and the ItemsPresenter child; style-surface follow-up)");
            }
        }

        /// <summary>Parses a converted Shape template child (its ElementStart
        /// is consumed): resolves the CLR type like entry values (engine
        /// short names and converted full names, never fabricated),
        /// requires a Shape-derived visual, instantiates it (the staged
        /// CircleProgressBar Arcs are the converted
        /// HandyControl.Expression.Shapes.Arc with a default constructor),
        /// lands TemplateBinding values as (element, source-name) pairs on
        /// the template like the XAML loader path (inert until BAML-tree
        /// inflation lands), lands PropertyWithConverter simples through the
        /// DP store (the staged Arcs carry ArcThickness TemplateBindings
        /// plus Stretch/EndAngle/StrokeThickness simples, with PART_Indicator
        /// joining the template namescope), lands complex Binding children
        /// as transfers on the retained visual, and closes on ElementEnd.
        /// Anything richer fails precise.</summary>
        private Shape ParseTemplateConvertedShape(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names, short typeId)
        {
            var (elementType, _) = ResolveEntryType(offset, $"<{TypeInfoName(typeId)}> on the template root panel on setter '{setterDisplay}' in style entry {entryDisplay}", typeId);
            if (!typeof(Shape).IsAssignableFrom(elementType))
                return Fail<Shape>(offset, $"element <{TypeInfoName(typeId)}> on the template root panel on setter '{setterDisplay}' in style entry {entryDisplay} is not a Shape (root panels take converted Shape children; style-surface follow-up)");
            Shape shape;
            try { shape = (Shape)Activator.CreateInstance(elementType, nonPublic: true)!; }
            catch (Exception ex)
            {
                return Fail<Shape>(offset, $"cannot instantiate template element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
            if (shape is not DependencyObject target)
                return Fail<Shape>(offset, $"template element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} is not a DependencyObject (template elements land through the DP store; style-surface follow-up)");
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return shape;
                if (record == 0x23)
                {
                    var (targetName, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, elementType);
                    bindings.Add((shape, source));
                    _ = targetName;
                    continue;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, elementType, attrId);
                    var value = ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property);
                    target.SetValue(property, value);
                    if (property.Name == "Name" && value is string name)
                    {
                        if (conv != ConvString)
                            Fail<object>(recordOffset, $"template Shape Name '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (names take the StringConverter; style-surface follow-up)");
                        if (names.ContainsKey(name))
                            Fail<object>(recordOffset, $"duplicate template part name '{name}' on setter '{setterDisplay}' in style entry {entryDisplay} (template names take unique names; style-surface follow-up)");
                        names[name] = shape;
                    }
                    continue;
                }
                if (record == 0x07)
                {
                    var (property, value) = ReadTemplateComplexChild(recordOffset, entryDisplay, setterDisplay, elementType);
                    if (value is Binding binding) BindingOperations.SetBinding(target, property, binding);
                    else
                        Fail<object>(recordOffset, $"template complex property '{property.Name}' on setter '{setterDisplay}' in style entry {entryDisplay} holds a {value.GetType().Name} (template complex values take bindings; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template converted Shape holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (shapes take TemplateBinding values plus simple values plus complex bindings; style-surface follow-up)");
            }
        }

        /// <summary>Parses a TextBlock template child (its ElementStart is
        /// consumed): HorizontalAlignment/VerticalAlignment/Visibility/TextTrimming
        /// enum simples plus IsHitTestVisible bools land through the DP store
        /// (the staged CircleProgressBar TextBlock centers itself; the staged
        /// ComboBox template TextBlock lands IsHitTestVisible plus TextTrimming
        /// plus the Text TemplateBinding here), TemplateBinding values land as
        /// (element, source-name) pairs on the template like the XAML loader path
        /// (the staged CircleProgressBar child binds Foreground/Text to the
        /// templated parent), complex Binding children land as transfers on the
        /// retained visual (the staged child binds Visibility to the templated
        /// parent's ShowText through the Boolean2VisibilityConverter static), and
        /// ElementEnd closes; the same parser also serves template-root TextBlocks.
        /// Anything richer fails precise.</summary>
        private TextBlock ParseTemplateTextBlock(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names)
        {
            _ = offset;
            _ = names;
            var text = new TextBlock();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return text;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var value = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId == KnownTextTrimmingMember && conv == ConvEnum)
                    {
                        var (trimProperty, trimDisplay) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(TextBlock), attrId);
                        text.SetValue(trimProperty, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, trimDisplay, value, conv, trimProperty));
                        continue;
                    }
                    if (conv == ConvBool && attrId >= 0)
                    {
                        var (boolProperty, boolDisplay) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(TextBlock), attrId);
                        if (!ReferenceEquals(boolProperty, UIElement.IsHitTestVisibleProperty))
                            Fail<object>(recordOffset, $"template TextBlock property '{boolDisplay}' with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (text blocks take HorizontalAlignment/VerticalAlignment/Visibility/TextTrimming enum values plus IsHitTestVisible bools; style-surface follow-up)");
                        text.SetValue(boolProperty, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, boolDisplay, value, conv, boolProperty));
                        continue;
                    }
                    var alignName = attrId switch
                    {
                        KnownHAlignMember => "HorizontalAlignment",
                        KnownVAlignMember => "VerticalAlignment",
                        KnownVisibilityMember => "Visibility",
                        _ => null,
                    };
                    if (alignName is null || conv != ConvEnum)
                        Fail<object>(recordOffset, $"template TextBlock property id {attrId} with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (text blocks take HorizontalAlignment/VerticalAlignment/Visibility/TextTrimming enum values plus IsHitTestVisible bools; style-surface follow-up)");
                    var align = DependencyProperty.FromName(typeof(TextBlock), alignName!);
                    if (align is null)
                        Fail<object>(recordOffset, $"'{alignName}' is not a dependency property of TextBlock on setter '{setterDisplay}' in style entry {entryDisplay}");
                    text.SetValue(align!, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, alignName!, value, conv, align!));
                    continue;
                }
                if (record == 0x23)
                {
                    var (target, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, typeof(TextBlock));
                    bindings.Add((text, source));
                    _ = target;
                    continue;
                }
                if (record == 0x07)
                {
                    var (property, value) = ReadTemplateComplexChild(recordOffset, entryDisplay, setterDisplay, typeof(TextBlock));
                    if (value is Binding binding) BindingOperations.SetBinding(text, property, binding);
                    else
                        Fail<object>(recordOffset, $"template complex property '{property.Name}' on setter '{setterDisplay}' in style entry {entryDisplay} holds a {value.GetType().Name} (template complex values take bindings; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template TextBlock holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (text blocks take alignment enum values plus TemplateBinding values plus complex bindings; style-surface follow-up)");
            }
        }

        /// <summary>Parses a Grid template visual (its ElementStart is
        /// consumed): the RowDefinitions list hosts RowDefinition children
        /// (Height simples through the GridLengthConverter; empty rows keep
        /// the star default), the ColumnDefinitions list hosts
        /// ColumnDefinition children (Width simples through the
        /// GridLengthConverter; empty columns keep the star default), the
        /// Children content member hosts Border children (Grid.Row/Grid.Column
        /// attached offsets land through the shared Grid attached store),
        /// closed by ElementEnd. The staged 'GroupBoxBaseTopTemplate' entry
        /// is the RowDefinitions shape (two RowDefinitions plus two Borders)
        /// and the staged 'GroupBoxBaseLeftTemplate' entry is the
        /// ColumnDefinitions shape (two ColumnDefinitions plus two Borders).
        /// Richer children are precise follow-ups.</summary>
        private Grid ParseTemplateGrid(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names)
        {
            _ = offset;
            var grid = new Grid();
            var rowsOpen = false;
            var colsOpen = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (rowsOpen)
                        Fail<object>(recordOffset, $"unclosed RowDefinitions list on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (row lists close with PropertyIListEnd; style-surface follow-up)");
                    if (colsOpen)
                        Fail<object>(recordOffset, $"unclosed ColumnDefinitions list on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (column lists close with PropertyIListEnd; style-surface follow-up)");
                    return grid;
                }
                if (record == 0x0B)
                {
                    var attrId = ReadI16();
                    if (IsRowDefinitionsAttribute(attrId))
                    {
                        if (rowsOpen)
                            Fail<object>(recordOffset, $"duplicate RowDefinitions list on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (template grids take one RowDefinitions list plus one ColumnDefinitions list; style-surface follow-up)");
                        rowsOpen = true;
                        continue;
                    }
                    if (IsColumnDefinitionsAttribute(attrId))
                    {
                        if (colsOpen)
                            Fail<object>(recordOffset, $"duplicate ColumnDefinitions list on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (template grids take one RowDefinitions list plus one ColumnDefinitions list; style-surface follow-up)");
                        colsOpen = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"template Grid holds list {TemplateAttributeDisplay(attrId)} on setter '{setterDisplay}' in style entry {entryDisplay} (template grids take the RowDefinitions/ColumnDefinitions lists; style-surface follow-up)");
                }
                if (record == 0x0C)
                {
                    if (!rowsOpen && !colsOpen)
                        Fail<object>(recordOffset, $"list end without a list on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (row lists open with PropertyIListStart; style-surface follow-up)");
                    rowsOpen = false;
                    colsOpen = false;
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownChildren)
                        Fail<object>(recordOffset, $"content member {member} on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (grids take the Children content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (rowsOpen)
                    {
                        if (childType != KnownRowDefinition)
                            Fail<object>(recordOffset, $"element {TargetTypeName(childType)} in the RowDefinitions list on setter '{setterDisplay}' in style entry {entryDisplay} (row lists take RowDefinition children; style-surface follow-up)");
                        grid.RowDefinitions.Add(ParseTemplateRowDefinition(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    if (colsOpen)
                    {
                        if (childType != KnownColumnDefinition)
                            Fail<object>(recordOffset, $"element {TargetTypeName(childType)} in the ColumnDefinitions list on setter '{setterDisplay}' in style entry {entryDisplay} (column lists take ColumnDefinition children; style-surface follow-up)");
                        grid.ColumnDefinitions.Add(ParseTemplateColumnDefinition(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    if (childType != KnownBorder)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} on the template Grid on setter '{setterDisplay}' in style entry {entryDisplay} (template grids take Border children; style-surface follow-up)");
                    grid.Children.Add(ParseTemplateBorder(recordOffset, entryDisplay, setterDisplay, bindings, names));
                    continue;
                }
                Fail<object>(recordOffset, $"template Grid holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (template grids take the RowDefinitions/ColumnDefinitions lists plus the Children content plus Border children; style-surface follow-up)");
            }
        }

        /// <summary>Parses a RowDefinition inside a template Grid (its
        /// ElementStart is consumed): Height simples convert through the
        /// GridLengthConverter ("Auto" plus star/pixel lengths like the XAML
        /// loader); empty rows keep the star default. Closed by ElementEnd.</summary>
        private RowDefinition ParseTemplateRowDefinition(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var row = new RowDefinition();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return row;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId != KnownRowHeightMember)
                        Fail<object>(recordOffset, $"template RowDefinition property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (row definitions take Height; style-surface follow-up)");
                    if (conv != ConvGridLength)
                        Fail<object>(recordOffset, $"template RowDefinition Height '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (row heights take the GridLengthConverter; style-surface follow-up)");
                    try { row.Height = (GridLength)new GridLengthConverter().ConvertFrom(null, System.Globalization.CultureInfo.InvariantCulture, text)!; }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or OverflowException)
                    {
                        return Fail<RowDefinition>(recordOffset, $"'{text}' is not a GridLength for Height on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
                    }
                    continue;
                }
                Fail<object>(recordOffset, $"template RowDefinition holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (row definitions take Height; style-surface follow-up)");
            }
        }

        /// <summary>True for the RowDefinitions list attribute on a template
        /// Grid (per-file AttributeInfo row owned by Grid).</summary>
        private bool IsRowDefinitionsAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownGrid && string.Equals(name, "RowDefinitions", StringComparison.Ordinal);
        }

        /// <summary>Parses a ColumnDefinition inside a template Grid (its
        /// ElementStart is consumed): Width simples convert through the
        /// GridLengthConverter ("Auto" plus star/pixel lengths like the XAML
        /// loader); empty columns keep the star default. Closed by ElementEnd.</summary>
        private ColumnDefinition ParseTemplateColumnDefinition(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var column = new ColumnDefinition();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return column;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId != KnownColumnWidthMember)
                        Fail<object>(recordOffset, $"template ColumnDefinition property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (column definitions take Width; style-surface follow-up)");
                    if (conv != ConvGridLength)
                        Fail<object>(recordOffset, $"template ColumnDefinition Width '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (column widths take the GridLengthConverter; style-surface follow-up)");
                    try { column.Width = (GridLength)new GridLengthConverter().ConvertFrom(null, System.Globalization.CultureInfo.InvariantCulture, text)!; }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or OverflowException)
                    {
                        return Fail<ColumnDefinition>(recordOffset, $"'{text}' is not a GridLength for Width on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
                    }
                    continue;
                }
                Fail<object>(recordOffset, $"template ColumnDefinition holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (column definitions take Width; style-surface follow-up)");
            }
        }

        /// <summary>True for the ColumnDefinitions list attribute on a template
        /// Grid (per-file AttributeInfo row owned by Grid).</summary>
        private bool IsColumnDefinitionsAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownGrid && string.Equals(name, "ColumnDefinitions", StringComparison.Ordinal);
        }

        /// <summary>True for the RenderTransformOrigin value attribute on a
        /// template ContentPresenter (per-file AttributeInfo row owned by
        /// UIElement).</summary>
        private bool IsRenderTransformOriginAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownUIElement && string.Equals(name, "RenderTransformOrigin", StringComparison.Ordinal);
        }

        /// <summary>True for the Angle value attribute on a template
        /// RotateTransform (per-file AttributeInfo row owned by the
        /// RotateTransform well-known owner).</summary>
        private bool IsRotateAngleAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownRotateTransform && string.Equals(name, "Angle", StringComparison.Ordinal);
        }

        /// <summary>Parses a ContentPresenter inside a template visual (its
        /// ElementStart is consumed): ContentSource string properties land
        /// verbatim (the staged "ActualContent"/"CheckedContent" names the
        /// templated parent's content slot and resolves at inflation, like the
        /// ContentSource mirror); Name strings land on the presenter and join
        /// the template namescope (the staged ToggleBlock presenters carry
        /// "ContentPresenterChecked"/"UnChecked"/"Indeterminate" for the
        /// ControlTemplate trigger TargetNames); RecognizesAccessKey lands on
        /// the plain CLR slot (the staged GroupBox header presenters carry
        /// "True"); RenderTransformOrigin lands on the plain CLR slot (the
        /// staged LeftTemplate header presenter carries "0.5,0.5");
        /// converted attached-DP PWCs (the staged Sprite
        /// IsDragElement) land in the DP store; complex Binding children
        /// land as transfers on the retained visual (the staged GroupBox
        /// header presenter binds Margin to the templated parent's Padding)
        /// and complex TransformGroup children land on the LayoutTransform
        /// slot (the staged LeftTemplate header presenter carries a -90-degree
        /// rotation);
        /// TemplateBinding values land as (element, source-name) pairs on
        /// the template like the XAML loader path (inert until BAML-tree
        /// inflation lands), closed by ElementEnd.</summary>
        private ContentPresenter ParseTemplateContentPresenter(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names)
        {
            _ = offset;
            var presenter = new ContentPresenter();
            var haveSource = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return presenter;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId == KnownContentSource && conv == ConvString)
                    {
                        if (haveSource)
                            Fail<object>(recordOffset, $"duplicate ContentSource on the template ContentPresenter on setter '{setterDisplay}' in style entry {entryDisplay}");
                        presenter.ContentSource = text;
                        haveSource = true;
                        continue;
                    }
                    if (attrId == KnownName && conv == ConvString)
                    {
                        if (names.ContainsKey(text))
                            Fail<object>(recordOffset, $"duplicate template part name '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} (template names take unique names; style-surface follow-up)");
                        presenter.Name = text;
                        names[text] = presenter;
                        continue;
                    }
                    var alignName = attrId switch
                    {
                        KnownHAlignMember => "HorizontalAlignment",
                        KnownVAlignMember => "VerticalAlignment",
                        KnownVisibilityMember => "Visibility",
                        _ => null,
                    };
                    if (alignName is not null && conv == ConvEnum)
                    {
                        var align = DependencyProperty.FromName(typeof(ContentPresenter), alignName);
                        if (align is null)
                            Fail<object>(recordOffset, $"'{alignName}' is not a dependency property of ContentPresenter on setter '{setterDisplay}' in style entry {entryDisplay}");
                        presenter.SetValue(align!, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, alignName, text, conv, align!));
                        continue;
                    }
                    if (attrId == KnownRecognizesAccessKeyMember)
                    {
                        // RecognizesAccessKey (the staged GroupBox header
                        // presenters carry "True"): a plain CLR slot like
                        // ContentSource, retained on the presenter.
                        if (conv != ConvBool)
                            Fail<object>(recordOffset, $"template ContentPresenter RecognizesAccessKey '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (access-key flags take the BooleanConverter; style-surface follow-up)");
                        if (!bool.TryParse(text, out var access))
                            Fail<object>(recordOffset, $"template ContentPresenter RecognizesAccessKey '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} is not a boolean (access-key flags take True/False; style-surface follow-up)");
                        presenter.RecognizesAccessKey = access;
                        continue;
                    }
                    if (IsRenderTransformOriginAttribute(attrId))
                    {
                        // RenderTransformOrigin (the staged GroupBox LeftTemplate
                        // header presenter carries "0.5,0.5"): a PointConverter
                        // value landing on the plain CLR slot like the XAML loader.
                        if (conv != ConvPoint)
                            Fail<object>(recordOffset, $"template ContentPresenter RenderTransformOrigin '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (render-transform origins take the PointConverter; style-surface follow-up)");
                        try { presenter.RenderTransformOrigin = Point.Parse(text); }
                        catch (FormatException ex)
                        {
                            return Fail<ContentPresenter>(recordOffset, $"'{text}' is not a Point for RenderTransformOrigin on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
                        }
                        continue;
                    }
                    if (attrId != KnownContentSource && attrId != KnownName && alignName is null)
                    {
                        // Converted attached-DP PWCs (the staged Sprite
                        // template carries WindowAttach.IsDragElement): the
                        // attribute resolves through the per-file table like
                        // other template simples and lands in the DP store.
                        var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(ContentPresenter), attrId);
                        presenter.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                        continue;
                    }
                    Fail<object>(recordOffset, $"template ContentPresenter property id {attrId} with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (presenters take ContentSource/Name/RecognizesAccessKey strings plus HorizontalAlignment/VerticalAlignment/Visibility enum values; style-surface follow-up)");
                    throw new InvalidOperationException("unreachable");
                }
                if (record == 0x23)
                {
                    var (target, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, typeof(ContentPresenter));
                    bindings.Add((presenter, source));
                    _ = target;
                    continue;
                }
                if (record == 0x07)
                {
                    // Complex Binding children (the staged GroupBox header
                    // presenter binds Margin to the templated parent's
                    // Padding): the transfer lands on the retained visual,
                    // inert until BAML-tree inflation lands. A complex
                    // TransformGroup child (the staged LeftTemplate header
                    // presenter carries LayoutTransform with a -90-degree
                    // RotateTransform) lands on the LayoutTransform slot.
                    var (property, value) = ReadTemplateComplexChild(recordOffset, entryDisplay, setterDisplay, typeof(ContentPresenter));
                    if (value is Binding binding) BindingOperations.SetBinding(presenter, property, binding);
                    else if (value is Transform transform && ReferenceEquals(property, FrameworkElement.LayoutTransformProperty)) presenter.SetValue(property, transform);
                    else
                        Fail<object>(recordOffset, $"template complex property '{property.Name}' on setter '{setterDisplay}' in style entry {entryDisplay} holds a {value.GetType().Name} (template complex values take bindings; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template ContentPresenter holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (presenters take ContentSource/Name/RecognizesAccessKey plus TemplateBinding values plus complex bindings; style-surface follow-up)");
            }
        }

        /// <summary>Reads a PropertyWithExtension TemplateBinding on a
        /// template visual (fixed 3xU16, no size prefix): the target travels
        /// as a well-known member id (ContentTemplate/Margin/Content/Height/
        /// Width/TextElement.Foreground/HorizontalAlignment/VerticalAlignment
        /// on a ContentPresenter - the staged GroupBox header binds the
        /// alignments to the templated parent's content alignments -
        /// Background/BorderBrush/BorderThickness/Height/Width/Margin/
        /// HorizontalAlignment/VerticalAlignment on a Border,
        /// Fill/Height/Width on a Rectangle,
        /// Margin/HorizontalAlignment/VerticalAlignment on a Panel) or a
        /// per-file AttributeInfo index (the staged GroupBox template binds
        /// SnapsToDevicePixels through its UIElement-owned row), resolved
        /// against the visual like WPF (attached dotted targets skip the DP
        /// check and resolve at inflation), and must name the
        /// TemplateBinding extension; the source travels as a well-known
        /// member id for framework-known templated-parent properties
        /// (ContentTemplate/Padding/Background/BorderBrush/BorderThickness/
        /// Content/Height/Width/HorizontalContentAlignment/
        /// VerticalContentAlignment/Foreground) or a per-file AttributeInfo
        /// index otherwise (FloatingBlock.ContentTemplate on the staged #105
        /// entry, the staged ActualHeight/ActualWidth rows on the
        /// CompareSlider template). Returns the target display plus the
        /// source property name; the caller records the (element, source)
        /// pair on the template like the XAML loader path, inert until
        /// BAML-tree inflation lands.</summary>
        private (string Target, string Source) ReadTemplateExtension(int offset, string entryDisplay, string setterDisplay, Type elementType)
        {
            var targetId = ReadI16();
            var extensionId = ReadU16();
            var sourceRef = ReadI16();
            if (extensionId != KnownTemplateBindingExtension)
                Fail<object>(offset, $"template {elementType.Name} holds extension {extensionId} on setter '{setterDisplay}' in style entry {entryDisplay} (template extensions take TemplateBinding; style-surface follow-up)");
            string? targetName = null;
            if (targetId >= 0)
            {
                // Per-file template targets (the staged GroupBox template
                // binds SnapsToDevicePixels through its per-file
                // UIElement-owned AttributeInfo row): the name travels in the
                // table and resolves against the visual like well-known ids.
                if (targetId >= _attrs.Count)
                    Fail<object>(offset, $"template extension target id {targetId} on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                targetName = _attrs[targetId].Name;
                if (string.IsNullOrEmpty(targetName))
                    Fail<object>(offset, $"template extension target id {targetId} on setter '{setterDisplay}' in style entry {entryDisplay} names nothing (style-surface follow-up)");
            }
            else if (elementType == typeof(ContentPresenter))
            {
                if (targetId == KnownContentTemplateMember) targetName = "ContentTemplate";
                else if (targetId == KnownMarginAttribute) targetName = "Margin";
                else if (targetId == KnownPresenterContentMember) targetName = "Content";
                else if (targetId == KnownHeightMember) targetName = "Height";
                else if (targetId == KnownWidthMember) targetName = "Width";
                else if (targetId == KnownTextForegroundMember) targetName = "TextElement.Foreground";
                else if (targetId == KnownHAlignMember) targetName = "HorizontalAlignment";
                else if (targetId == KnownVAlignMember) targetName = "VerticalAlignment";
            }
            else if (elementType == typeof(Border))
            {
                if (targetId == KnownBorderBackgroundMember) targetName = "Background";
                else if (targetId == KnownBorderBrushMember) targetName = "BorderBrush";
                else if (targetId == KnownBorderThicknessMember) targetName = "BorderThickness";
                else if (targetId == KnownHeightMember) targetName = "Height";
                else if (targetId == KnownWidthMember) targetName = "Width";
                else if (targetId == KnownMarginAttribute) targetName = "Margin";
                else if (targetId == KnownHAlignMember) targetName = "HorizontalAlignment";
                else if (targetId == KnownVAlignMember) targetName = "VerticalAlignment";
            }
            else if (elementType == typeof(Rectangle))
            {
                if (targetId == KnownFillMember) targetName = "Fill";
                else if (targetId == KnownStrokeMember) targetName = "Stroke";
                else if (targetId == KnownHeightMember) targetName = "Height";
                else if (targetId == KnownWidthMember) targetName = "Width";
            }
            else if (typeof(Shape).IsAssignableFrom(elementType))
            {
                // The staged CircleProgressBar Arcs bind Stroke/Fill/Width/
                // Height to the templated parent (converted Arc resolves
                // through the engine Shape DPs, which slice-134 declares).
                if (targetId == KnownFillMember) targetName = "Fill";
                else if (targetId == KnownStrokeMember) targetName = "Stroke";
                else if (targetId == KnownHeightMember) targetName = "Height";
                else if (targetId == KnownWidthMember) targetName = "Width";
            }
            else if (elementType == typeof(TextBlock))
            {
                // The staged CircleProgressBar TextBlock binds Foreground/
                // Text to the templated parent (TextBlock.Foreground is an
                // engine DP since slice-134, like Shape.Stroke above).
                if (targetId == KnownTextBlockForegroundMember) targetName = "Foreground";
                else if (targetId == KnownTextAttribute) targetName = "Text";
            }
            else if (typeof(Panel).IsAssignableFrom(elementType))
            {
                if (targetId == KnownMarginAttribute) targetName = "Margin";
                else if (targetId == KnownHAlignMember) targetName = "HorizontalAlignment";
                else if (targetId == KnownVAlignMember) targetName = "VerticalAlignment";
            }
            if (targetName is null)
                Fail<object>(offset, $"template extension target id {targetId} on setter '{setterDisplay}' in style entry {entryDisplay} (template extension targets take ContentTemplate/Margin/Content/Height/Width/TextElement.Foreground/HorizontalAlignment/VerticalAlignment on a ContentPresenter and Background/BorderBrush/BorderThickness/Height/Width/Margin/HorizontalAlignment/VerticalAlignment on a Border and Fill/Height/Width/Stroke on a Rectangle and Fill/Stroke/Height/Width on a Shape and Foreground/Text on a TextBlock and Margin/HorizontalAlignment/VerticalAlignment on a Panel or a per-file AttributeInfo row; style-surface follow-up)");
            if (!targetName.Contains('.') && DependencyProperty.FromName(elementType, targetName) is null)
                Fail<object>(offset, $"'{targetName}' is not a dependency property of {elementType.Name} on setter '{setterDisplay}' in style entry {entryDisplay}");
            string sourceName;
            if (sourceRef >= 0)
            {
                if (sourceRef >= _attrs.Count)
                    Fail<object>(offset, $"template extension source id {sourceRef} on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                sourceName = _attrs[sourceRef].Name;
                if (string.IsNullOrEmpty(sourceName))
                    Fail<object>(offset, $"template extension source id {sourceRef} on setter '{setterDisplay}' in style entry {entryDisplay} names nothing (style-surface follow-up)");
            }
            else
            {
                sourceName = sourceRef switch
                {
                    KnownTemplateSourceContentTemplate => "ContentTemplate",
                    KnownPaddingMember => "Padding",
                    KnownBackgroundMember => "Background",
                    KnownBorderBrushSourceMember => "BorderBrush",
                    KnownBorderThicknessSourceMember => "BorderThickness",
                    KnownContent => "Content",
                    KnownHeightMember => "Height",
                    KnownWidthMember => "Width",
                    KnownHContentMember => "HorizontalContentAlignment",
                    KnownVContentMember => "VerticalContentAlignment",
                    KnownForegroundSourceMember => "Foreground",
                    _ => Fail<string>(offset, $"template extension source id {sourceRef} on setter '{setterDisplay}' in style entry {entryDisplay} (template extension sources take ContentTemplate/Padding/Background/BorderBrush/BorderThickness/Content/Height/Width/HorizontalContentAlignment/VerticalContentAlignment/Foreground or an AttributeInfo row; style-surface follow-up)"),
                };
            }
            return (targetName, sourceName);
        }

        /// <summary>Parses a StackPanel template visual (its ElementStart is
        /// consumed): PropertyWithConverter properties land through the DP
        /// store (Orientation via the per-file AttributeInfo table, Margin
        /// via the well-known member id), the Children content member hosts
        /// one per-file converted child element each, and anything richer
        /// fails precise. The staged #104 FlipClock template is this exact
        /// shape (Orientation/Horizontal with six FlipNumber children).</summary>
        private StackPanel ParseTemplateStackPanel(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var panel = new StackPanel();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(StackPanel), attrId);
                    panel.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownChildren)
                        Fail<object>(recordOffset, $"content member {member} on the template StackPanel on setter '{setterDisplay}' in style entry {entryDisplay} (panels take the Children content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType < 0)
                        Fail<object>(recordOffset, $"element {WellKnownName(childType)} on the template StackPanel on setter '{setterDisplay}' in style entry {entryDisplay} (template children are per-file elements; style-surface follow-up)");
                    var child = ParseTemplateElement(recordOffset, entryDisplay, setterDisplay, childType);
                    if (child is UIElement ui) panel.Children.Add(ui);
                    else
                        Fail<object>(recordOffset, $"element <{TypeInfoName(childType)}> on the template StackPanel on setter '{setterDisplay}' in style entry {entryDisplay} is not a visual (template children join Children; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template StackPanel holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (panels take properties plus Children; style-surface follow-up)");
            }
        }

        /// <summary>Parses one per-file template child element (its
        /// ElementStart is consumed): instantiates the CLR type (engine short
        /// names and converted full names, like entry values), lands
        /// PropertyWithConverter simples through the DP store and
        /// single-element complex children holding a Binding through the
        /// real binding path, and closes on ElementEnd. The staged #104
        /// FlipNumber children are this exact shape (Margin plus the Number
        /// Binding).</summary>
        private object ParseTemplateElement(int offset, string entryDisplay, string setterDisplay, short typeId)
        {
            var (elementType, _) = ResolveEntryType(offset, $"<{TypeInfoName(typeId)}> on the template on setter '{setterDisplay}' in style entry {entryDisplay}", typeId);
            object child;
            try { child = Activator.CreateInstance(elementType, nonPublic: true)!; }
            catch (Exception ex)
            {
                return Fail<object>(offset, $"cannot instantiate template element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
            if (child is not DependencyObject target)
                return Fail<object>(offset, $"template element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} is not a DependencyObject (template elements land through the DP store; style-surface follow-up)");
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return child;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, elementType, attrId);
                    target.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    var (property, value) = ReadTemplateComplexChild(recordOffset, entryDisplay, setterDisplay, elementType);
                    if (value is Binding binding) BindingOperations.SetBinding(target, property, binding);
                    else
                        Fail<object>(recordOffset, $"template complex property '{property.Name}' on setter '{setterDisplay}' in style entry {entryDisplay} holds a {value.GetType().Name} (template complex values take bindings; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template element <{TypeInfoName(typeId)}> holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (elements take properties plus complex children; style-surface follow-up)");
            }
        }

        /// <summary>Parses a Border template visual (its ElementStart is
        /// consumed): Background/BorderBrush/BorderThickness TemplateBinding
        /// values land as (element, source-name) pairs on the template like
        /// the XAML loader path (inert until BAML-tree inflation lands), the
        /// Child content member hosts one StackPanel, Rectangle or per-file
        /// Panel child (the staged ToggleBlock Border hosts the converted
        /// SimplePanel, the staged GroupBox borders host header/content
        /// ContentPresenters), a PropertyCustom packed-ARGB brush value lands
        /// the Background (the staged RepeatButton Border carries a literal
        /// Transparent brush), Margin/Grid.Row simples land through the
        /// DP/attached stores, complex Binding children land as transfers on
        /// the retained visual (the staged GroupBox borders bind
        /// MinHeight/Background/CornerRadius to the templated parent), and
        /// ElementEnd closes. The staged SimpleItemsControl template is this
        /// exact shape (Border with the three TemplateBindings and a
        /// PART_Panel StackPanel). Anything richer fails precise.</summary>
        private Border ParseTemplateBorder(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names)
        {
            _ = offset;
            var border = new Border();
            var haveChild = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return border;
                if (record == 0x23)
                {
                    var (target, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, typeof(Border));
                    bindings.Add((border, source));
                    _ = target;
                    continue;
                }
                if (record == 0x06)
                {
                    border.Background = (Brush)ReadTemplateBrushValue(recordOffset, entryDisplay, setterDisplay, typeof(Border));
                    continue;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId == KnownGridRowMember)
                    {
                        // Grid.Row attached offset on a template child (the
                        // staged GroupBox template docks its second Border in
                        // row 1): an Int32Converter value landing through the
                        // shared Grid attached store like the XAML loader.
                        if (conv != ConvInt32)
                            Fail<object>(recordOffset, $"template Border Grid.Row '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (Grid.Row takes the Int32Converter; style-surface follow-up)");
                        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var row))
                            Fail<object>(recordOffset, $"template Border Grid.Row '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} is not an integer (Grid.Row takes an integer; style-surface follow-up)");
                        Grid.SetRow(border, row);
                        continue;
                    }
                    if (attrId == KnownGridColumnMember)
                    {
                        // Grid.Column attached offset on a template child (the
                        // staged GroupBox LeftTemplate docks its second Border
                        // in column 1): an Int32Converter value landing through
                        // the shared Grid attached store like the XAML loader.
                        if (conv != ConvInt32)
                            Fail<object>(recordOffset, $"template Border Grid.Column '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (Grid.Column takes the Int32Converter; style-surface follow-up)");
                        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var column))
                            Fail<object>(recordOffset, $"template Border Grid.Column '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} is not an integer (Grid.Column takes an integer; style-surface follow-up)");
                        Grid.SetColumn(border, column);
                        continue;
                    }
                    if (attrId == KnownClipToBoundsMember)
                    {
                        // ClipToBounds on a template Border (the staged GroupBox
                        // LeftTemplate header Border carries "False"): a
                        // BooleanConverter value landing on the plain CLR slot
                        // like the XAML loader.
                        if (conv != ConvBool)
                            Fail<object>(recordOffset, $"template Border ClipToBounds '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (ClipToBounds takes the BooleanConverter; style-surface follow-up)");
                        if (!bool.TryParse(text, out var clip))
                            Fail<object>(recordOffset, $"template Border ClipToBounds '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} is not a boolean (ClipToBounds takes True/False; style-surface follow-up)");
                        border.ClipToBounds = clip;
                        continue;
                    }
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(Border), attrId);
                    border.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x07)
                {
                    // Complex Binding children (the staged GroupBox template
                    // binds MinHeight/MinWidth/Background/CornerRadius to the templated
                    // parent): the transfer lands on the retained visual like
                    // the generic template-element path, inert until BAML-tree
                    // inflation lands.
                    var (property, value) = ReadTemplateComplexChild(recordOffset, entryDisplay, setterDisplay, typeof(Border));
                    if (value is Binding binding) BindingOperations.SetBinding(border, property, binding);
                    else
                        Fail<object>(recordOffset, $"template complex property '{property.Name}' on setter '{setterDisplay}' in style entry {entryDisplay} holds a {value.GetType().Name} (template complex values take bindings; style-surface follow-up)");
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownBorderChildContent)
                        Fail<object>(recordOffset, $"content member {member} on the template Border on setter '{setterDisplay}' in style entry {entryDisplay} (borders take the Child content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    if (haveChild)
                        Fail<object>(recordOffset, $"second visual on the template Border on setter '{setterDisplay}' in style entry {entryDisplay} (borders take one child; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType == KnownStackPanel)
                    {
                        var panel = ParseTemplateStackPanel(recordOffset, entryDisplay, setterDisplay);
                        border.Child = panel;
                    }
                    else if (childType == KnownRectangle)
                    {
                        border.Child = ParseTemplateRectangle(recordOffset, entryDisplay, setterDisplay, bindings);
                    }
                    else if (childType == KnownContentPresenter)
                    {
                        border.Child = ParseTemplateContentPresenter(recordOffset, entryDisplay, setterDisplay, bindings, names);
                    }
                    else if (childType == KnownViewport3D)
                    {
                        border.Child = ParseTemplateViewport3D(recordOffset, entryDisplay, setterDisplay, names);
                    }
                    else if (childType >= 0)
                    {
                        border.Child = ParseTemplateSimplePanel(recordOffset, entryDisplay, setterDisplay, bindings, names, childType);
                    }
                    else
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} on the template Border on setter '{setterDisplay}' in style entry {entryDisplay} (template borders take a StackPanel, Rectangle, ContentPresenter, SimplePanel or Viewport3D child; style-surface follow-up)");
                    haveChild = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template Border holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (borders take TemplateBinding values plus Margin/Grid.Row simples plus complex bindings plus one StackPanel, Rectangle, ContentPresenter, SimplePanel or Viewport3D child; style-surface follow-up)");
            }
        }

        /// <summary>Parses a per-file Panel template visual (its ElementStart
        /// is consumed): resolves the CLR type like entry values (engine
        /// short names and converted full names, never fabricated),
        /// requires a Panel-derived visual, instantiates it (the converted
        /// FlipNumber precedent: single match with a default constructor),
        /// lands TemplateBinding values as (element, source-name) pairs on
        /// the template like the XAML loader path (inert until BAML-tree
        /// inflation lands), hosts ContentPresenter children through the
        /// Children content member with their names joining the template
        /// namescope, and closes on ElementEnd. The staged ToggleBlock
        /// template is this exact shape (converted SimplePanel with Margin/
        /// VerticalAlignment/HorizontalAlignment TemplateBindings over
        /// Padding/VerticalContentAlignment/HorizontalContentAlignment plus
        /// three named ContentPresenters). Anything richer fails precise.
        /// </summary>
        private Panel ParseTemplateSimplePanel(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings, Dictionary<string, object> names, short typeId)
        {
            var (elementType, _) = ResolveEntryType(offset, $"<{TypeInfoName(typeId)}> on the template Border on setter '{setterDisplay}' in style entry {entryDisplay}", typeId);
            if (!typeof(Panel).IsAssignableFrom(elementType))
                return Fail<Panel>(offset, $"element <{TypeInfoName(typeId)}> on the template Border on setter '{setterDisplay}' in style entry {entryDisplay} is not a Panel (template borders take a StackPanel, Rectangle or SimplePanel child; style-surface follow-up)");
            Panel panel;
            try { panel = (Panel)Activator.CreateInstance(elementType, nonPublic: true)!; }
            catch (Exception ex)
            {
                return Fail<Panel>(offset, $"cannot instantiate template element '{elementType.FullName}' on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.GetBaseException().Message})");
            }
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return panel;
                if (record == 0x23)
                {
                    var (target, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, elementType);
                    bindings.Add((panel, source));
                    _ = target;
                    continue;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownChildren)
                        Fail<object>(recordOffset, $"content member {member} on the template panel on setter '{setterDisplay}' in style entry {entryDisplay} (panels take the Children content; style-surface follow-up)");
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownContentPresenter)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} on the template panel on setter '{setterDisplay}' in style entry {entryDisplay} (template panels take ContentPresenter children; style-surface follow-up)");
                    var presenter = ParseTemplateContentPresenter(recordOffset, entryDisplay, setterDisplay, bindings, names);
                    panel.Children.Add(presenter);
                    continue;
                }
                Fail<object>(recordOffset, $"template panel holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (panels take TemplateBinding values plus ContentPresenter children; style-surface follow-up)");
            }
        }

        /// <summary>Reads a PropertyCustom packed-ARGB brush value inside a
        /// template visual (sized I16 attribute + U16 serializer + 5 bytes):
        /// the attribute must be the Background well-known member of the
        /// visual type and the serializer the packed-ARGB brush serializer
        /// (744), landing a SolidColorBrush through the DP store like the
        /// style-setter brush path. Anything else fails precise so a
        /// half-built visual never lands.</summary>
        private object ReadTemplateBrushValue(int offset, string entryDisplay, string setterDisplay, Type elementType)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var serializer = ReadU16();
            if (attrId != KnownBorderBackgroundMember || !ReferenceEquals(elementType, typeof(Border)))
                Fail<object>(offset, $"template {elementType.Name} holds a packed-ARGB brush for attribute id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (template brushes take the Background value; style-surface follow-up)");
            if (serializer != BrushValueSerializer)
                Fail<object>(offset, $"template {elementType.Name} holds a brush value with serializer {serializer} on setter '{setterDisplay}' in style entry {entryDisplay} (template brushes take packed-ARGB values; style-surface follow-up)");
            if (end - _pos != 5)
                Fail<object>(offset, $"template {elementType.Name} holds a {end - _pos}-byte brush value on setter '{setterDisplay}' in style entry {entryDisplay} (packed-ARGB brushes take 0x01 plus 4 bytes; style-surface follow-up)");
            var mark = ReadByte();
            var blue = ReadByte();
            var green = ReadByte();
            var red = ReadByte();
            var alpha = ReadByte();
            ExpectEnd(end, offset, "PropertyCustom");
            if (mark != 0x01)
                Fail<object>(offset, $"template {elementType.Name} holds a brush value marked 0x{mark:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (packed-ARGB brushes start with 0x01; style-surface follow-up)");
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            if (DependencyProperty.FromName(elementType, "Background") is null)
                Fail<object>(offset, $"'Background' is not a dependency property of {elementType.Name} on setter '{setterDisplay}' in style entry {entryDisplay}");
            return brush;
        }

        /// <summary>Parses a Viewport3D template child (its ElementStart is
        /// consumed): the Name string joining the template namescope, the
        /// Camera complex child holding one PerspectiveCamera and the Children
        /// list holding ModelVisual3D nodes, closed by ElementEnd. The staged
        /// keyidx-155 'CoverFlowBaseStyle' template Border is this exact shape
        /// (PART_Viewport3D with PART_Camera plus two DirectionalLight Content
        /// values and PART_VisualParent). The viewport lands retained on the
        /// template like other template visuals, inert until BAML-tree
        /// inflation lands (the engine Viewport3D is a wireframe shim, never
        /// a rasterizer). Anything else fails precise.</summary>
        private Viewport3D ParseTemplateViewport3D(int offset, string entryDisplay, string setterDisplay, Dictionary<string, object> names)
        {
            _ = offset;
            var viewport = new Viewport3D();
            var haveCamera = false;
            var haveChildren = false;
            var childrenOpen = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (childrenOpen)
                        Fail<object>(recordOffset, $"unclosed Children list on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (children lists close with PropertyIListEnd; style-surface follow-up)");
                    return viewport;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (!IsViewportNameAttribute(attrId) || conv != ConvString)
                        Fail<object>(recordOffset, $"template Viewport3D property id {attrId} with converter {conv} on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take the Name string; style-surface follow-up)");
                    if (names.ContainsKey(text))
                        Fail<object>(recordOffset, $"duplicate template part name '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} (template names take unique names; style-surface follow-up)");
                    var nameProperty = DependencyProperty.FromName(typeof(Viewport3D), "Name");
                    if (nameProperty is null)
                        Fail<object>(recordOffset, $"'Name' is not a dependency property of Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay}");
                    viewport.SetValue(nameProperty!, text);
                    names[text] = viewport;
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveCamera)
                        Fail<object>(recordOffset, $"duplicate Camera on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take one Camera; style-surface follow-up)");
                    var complexAttr = ReadI16();
                    if (!IsViewportCameraAttribute(complexAttr))
                        Fail<object>(recordOffset, $"attribute id {complexAttr} on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take the Camera complex; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"Camera child holds record 0x{childRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (the Camera complex takes one PerspectiveCamera; style-surface follow-up)");
                    var cameraType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (cameraType != KnownPerspectiveCamera)
                        Fail<object>(childOffset, $"element {TargetTypeName(cameraType)} in the Camera complex on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take a PerspectiveCamera; style-surface follow-up)");
                    viewport.Camera = ParseTemplatePerspectiveCamera(childOffset, entryDisplay, setterDisplay, names);
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"Camera complex on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    haveCamera = true;
                    continue;
                }
                if (record == 0x0B)
                {
                    var listAttr = ReadI16();
                    if (!IsViewportChildrenList(listAttr))
                        Fail<object>(recordOffset, $"template Viewport3D holds list {TemplateAttributeDisplay(listAttr)} on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take the Children list; style-surface follow-up)");
                    if (haveChildren)
                        Fail<object>(recordOffset, $"duplicate Children list on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take one Children list; style-surface follow-up)");
                    haveChildren = true;
                    childrenOpen = true;
                    continue;
                }
                if (record == 0x0C)
                {
                    if (!childrenOpen)
                        Fail<object>(recordOffset, $"list end without a list on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (children lists open with PropertyIListStart; style-surface follow-up)");
                    childrenOpen = false;
                    continue;
                }
                if (record == 0x03)
                {
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (!childrenOpen)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} outside the Children list on the template Viewport3D on setter '{setterDisplay}' in style entry {entryDisplay} (viewport children take ModelVisual3D nodes in the Children list; style-surface follow-up)");
                    if (childType != KnownModelVisual3D)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} in the Children list on setter '{setterDisplay}' in style entry {entryDisplay} (viewport children take ModelVisual3D nodes; style-surface follow-up)");
                    viewport.Children.Add(ParseTemplateModelVisual3D(recordOffset, entryDisplay, setterDisplay, names));
                    continue;
                }
                Fail<object>(recordOffset, $"template Viewport3D holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (viewports take the Name string plus the Camera complex plus one Children list of ModelVisual3D nodes; style-surface follow-up)");
            }
        }

        /// <summary>Parses a PerspectiveCamera inside a template Viewport3D
        /// Camera complex (its ElementStart is consumed): an optional
        /// DefAttribute name joining the template namescope plus
        /// Position/UpDirection/LookDirection/FieldOfView simples, closed by
        /// ElementEnd. The staged CoverFlow PART_Camera is this exact shape
        /// (Position "0,0,3" through the Point3DConverter, UpDirection
        /// "0,1,0"/LookDirection "0,0,-1" through the Vector3DConverter,
        /// FieldOfView "90" through the DoubleConverter). Values land on the
        /// retained camera like the XAML loader path; anything else fails
        /// precise.</summary>
        private PerspectiveCamera ParseTemplatePerspectiveCamera(int offset, string entryDisplay, string setterDisplay, Dictionary<string, object> names)
        {
            _ = offset;
            var camera = new PerspectiveCamera();
            var havePosition = false;
            var haveUp = false;
            var haveLook = false;
            var haveFov = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return camera;
                if (record == 0x19)
                {
                    var partName = ReadTemplateDefName(recordOffset, entryDisplay, setterDisplay, names);
                    names[partName] = camera;
                    continue;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"template property id {attrId} on the template camera on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                    var (ownerId, name) = _attrs[attrId];
                    if (ownerId == KnownProjectionCamera && string.Equals(name, "Position", StringComparison.Ordinal))
                    {
                        if (havePosition)
                            Fail<object>(recordOffset, $"duplicate Position on the template camera on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take one Position; style-surface follow-up)");
                        if (conv != ConvPoint3D)
                            Fail<object>(recordOffset, $"template camera Position '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (positions take the Point3DConverter; style-surface follow-up)");
                        try { camera.Position = Point3D.Parse(text); }
                        catch (FormatException ex)
                        {
                            return Fail<PerspectiveCamera>(recordOffset, $"'{text}' is not a Point3D for Position on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.Message})");
                        }
                        havePosition = true;
                        continue;
                    }
                    if (ownerId == KnownProjectionCamera && (string.Equals(name, "UpDirection", StringComparison.Ordinal) || string.Equals(name, "LookDirection", StringComparison.Ordinal)))
                    {
                        if (string.Equals(name, "UpDirection", StringComparison.Ordinal) ? haveUp : haveLook)
                            Fail<object>(recordOffset, $"duplicate {name} on the template camera on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take one {name}; style-surface follow-up)");
                        if (conv != ConvVector3D)
                            Fail<object>(recordOffset, $"template camera {name} '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (directions take the Vector3DConverter; style-surface follow-up)");
                        try
                        {
                            var direction = Vector3D.Parse(text);
                            if (string.Equals(name, "UpDirection", StringComparison.Ordinal)) camera.UpDirection = direction;
                            else camera.LookDirection = direction;
                        }
                        catch (FormatException ex)
                        {
                            return Fail<PerspectiveCamera>(recordOffset, $"'{text}' is not a Vector3D for {name} on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.Message})");
                        }
                        if (string.Equals(name, "UpDirection", StringComparison.Ordinal)) haveUp = true;
                        else haveLook = true;
                        continue;
                    }
                    if (ownerId == KnownPerspectiveCamera && string.Equals(name, "FieldOfView", StringComparison.Ordinal))
                    {
                        if (haveFov)
                            Fail<object>(recordOffset, $"duplicate FieldOfView on the template camera on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take one FieldOfView; style-surface follow-up)");
                        if (conv != ConvDouble)
                            Fail<object>(recordOffset, $"template camera FieldOfView '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (fields of view take the DoubleConverter; style-surface follow-up)");
                        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fov))
                            Fail<object>(recordOffset, $"'{text}' is not a Double for FieldOfView on setter '{setterDisplay}' in style entry {entryDisplay}");
                        camera.FieldOfView = fov;
                        haveFov = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"template camera property '{name}' on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take Position/UpDirection/LookDirection/FieldOfView; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template camera holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (cameras take a name plus Position/UpDirection/LookDirection/FieldOfView; style-surface follow-up)");
            }
        }

        /// <summary>Parses a ModelVisual3D inside a template Viewport3D
        /// Children list (its ElementStart is consumed): an optional
        /// DefAttribute name joining the template namescope plus the Content
        /// complex child holding one DirectionalLight, closed by ElementEnd.
        /// The staged CoverFlow children are this exact shape (two Content
        /// lights plus the named PART_VisualParent). Anything else fails
        /// precise.</summary>
        private ModelVisual3D ParseTemplateModelVisual3D(int offset, string entryDisplay, string setterDisplay, Dictionary<string, object> names)
        {
            _ = offset;
            var node = new ModelVisual3D();
            var haveContent = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return node;
                if (record == 0x19)
                {
                    var partName = ReadTemplateDefName(recordOffset, entryDisplay, setterDisplay, names);
                    names[partName] = node;
                    continue;
                }
                if (record == 0x07)
                {
                    if (haveContent)
                        Fail<object>(recordOffset, $"duplicate Content on the template ModelVisual3D on setter '{setterDisplay}' in style entry {entryDisplay} (visuals take one Content; style-surface follow-up)");
                    var complexAttr = ReadI16();
                    if (complexAttr < 0 || complexAttr >= _attrs.Count)
                        Fail<object>(recordOffset, $"template property id {complexAttr} on the template ModelVisual3D on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                    var (complexOwner, complexName) = _attrs[complexAttr];
                    if (complexOwner != KnownModelVisual3D || !string.Equals(complexName, "Content", StringComparison.Ordinal))
                        Fail<object>(recordOffset, $"attribute '{complexName}' on the template ModelVisual3D on setter '{setterDisplay}' in style entry {entryDisplay} (visuals take the Content complex; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"Content child holds record 0x{childRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (the Content complex takes one DirectionalLight; style-surface follow-up)");
                    var lightType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (lightType != KnownDirectionalLight)
                        Fail<object>(childOffset, $"element {TargetTypeName(lightType)} in the Content complex on setter '{setterDisplay}' in style entry {entryDisplay} (content takes a DirectionalLight; style-surface follow-up)");
                    node.Content = ParseTemplateDirectionalLight(childOffset, entryDisplay, setterDisplay);
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"Content complex on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    haveContent = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template ModelVisual3D holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (visuals take a name plus the Content complex; style-surface follow-up)");
            }
        }

        /// <summary>Parses a DirectionalLight inside a template ModelVisual3D
        /// Content complex (its ElementStart is consumed): Color through the
        /// ColorConverter plus Direction through the Vector3DConverter,
        /// closed by ElementEnd. The staged CoverFlow lights are this exact
        /// shape ("White" plus "1,0,-4"/"-1,0,-4"). Anything else fails
        /// precise.</summary>
        private DirectionalLight ParseTemplateDirectionalLight(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var light = new DirectionalLight();
            var haveColor = false;
            var haveDirection = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return light;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (attrId < 0 || attrId >= _attrs.Count)
                        Fail<object>(recordOffset, $"template property id {attrId} on the template light on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                    var (ownerId, name) = _attrs[attrId];
                    if (ownerId == KnownLight && string.Equals(name, "Color", StringComparison.Ordinal))
                    {
                        if (haveColor)
                            Fail<object>(recordOffset, $"duplicate Color on the template light on setter '{setterDisplay}' in style entry {entryDisplay} (lights take one Color; style-surface follow-up)");
                        if (conv != ConvColor)
                            Fail<object>(recordOffset, $"template light Color '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (colors take the ColorConverter; style-surface follow-up)");
                        light.Color = (Color)ParseColorEntry(recordOffset, entryDisplay, text);
                        haveColor = true;
                        continue;
                    }
                    if (ownerId == KnownDirectionalLight && string.Equals(name, "Direction", StringComparison.Ordinal))
                    {
                        if (haveDirection)
                            Fail<object>(recordOffset, $"duplicate Direction on the template light on setter '{setterDisplay}' in style entry {entryDisplay} (lights take one Direction; style-surface follow-up)");
                        if (conv != ConvVector3D)
                            Fail<object>(recordOffset, $"template light Direction '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (directions take the Vector3DConverter; style-surface follow-up)");
                        try { light.Direction = Vector3D.Parse(text); }
                        catch (FormatException ex)
                        {
                            return Fail<DirectionalLight>(recordOffset, $"'{text}' is not a Vector3D for Direction on setter '{setterDisplay}' in style entry {entryDisplay} ({ex.Message})");
                        }
                        haveDirection = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"template light property '{name}' on setter '{setterDisplay}' in style entry {entryDisplay} (lights take Color/Direction; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"template light holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (lights take Color/Direction; style-surface follow-up)");
            }
        }

        /// <summary>Reads a DefAttribute template part name (sized string
        /// Value plus the U16 NameId, ILSpy DefAttributeRecord layout): the
        /// staged keyidx-155 camera/part names plus every staged
        /// DefAttribute carry the unassigned 0xFFFF id, which fails precise
        /// otherwise. Duplicate names fail precise like the PWC Name path.
        /// The name joins the template namescope at the call site.</summary>
        private string ReadTemplateDefName(int recordOffset, string entryDisplay, string setterDisplay, Dictionary<string, object> names)
        {
            var end = StartSized(out _);
            var text = ReadAnsiString();
            var nameId = ReadU16();
            ExpectEnd(end, recordOffset, "DefAttribute");
            if (nameId != 0xFFFF)
                Fail<object>(recordOffset, $"template part '{text}' carries name id {nameId} on setter '{setterDisplay}' in style entry {entryDisplay} (template names take the unassigned id; style-surface follow-up)");
            if (names.ContainsKey(text))
                Fail<object>(recordOffset, $"duplicate template part name '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} (template names take unique names; style-surface follow-up)");
            return text;
        }

        /// <summary>True for the Name value attribute on a template
        /// Viewport3D (per-file AttributeInfo row owned by Viewport3D).
        /// </summary>
        private bool IsViewportNameAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownViewport3D && string.Equals(name, "Name", StringComparison.Ordinal);
        }

        /// <summary>True for the Camera complex attribute on a template
        /// Viewport3D (per-file AttributeInfo row owned by Viewport3D).
        /// </summary>
        private bool IsViewportCameraAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownViewport3D && string.Equals(name, "Camera", StringComparison.Ordinal);
        }

        /// <summary>True for the Children list attribute on a template
        /// Viewport3D (the well-known Children member id plus the
        /// per-file AttributeInfo row owned by Viewport3D, mirroring the
        /// Grid RowDefinitions/ColumnDefinitions table checks).</summary>
        private bool IsViewportChildrenList(short attrId)
        {
            if (attrId == KnownViewportChildrenMember) return true;
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownViewport3D && string.Equals(name, "Children", StringComparison.Ordinal);
        }
        /// <summary>Parses a Rectangle template visual (its ElementStart is
        /// consumed): PropertyWithConverter simples land through the DP store
        /// (the staged StrokeThickness "0" and Height/Width "2" travel as
        /// LengthConverter doubles) and TemplateBinding values land as
        /// (element, source-name) pairs on the template like the XAML loader
        /// path (inert until BAML-tree inflation lands), closed by
        /// ElementEnd. The staged RepeatButton templates are this exact
        /// shape (StrokeThickness/Fill/Height or Width on the Border child,
        /// Fill/Height/Width on the ScrollBarBase root). Anything richer
        /// fails precise.</summary>
        private Rectangle ParseTemplateRectangle(int offset, string entryDisplay, string setterDisplay, List<(object Element, string Property)> bindings)
        {
            _ = offset;
            var rectangle = new Rectangle();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return rectangle;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    var (property, display) = ReadTemplateProperty(recordOffset, entryDisplay, setterDisplay, typeof(Rectangle), attrId);
                    rectangle.SetValue(property, ConvertTemplateValue(recordOffset, entryDisplay, setterDisplay, display, text, conv, property));
                    continue;
                }
                if (record == 0x23)
                {
                    var (target, source) = ReadTemplateExtension(recordOffset, entryDisplay, setterDisplay, typeof(Rectangle));
                    bindings.Add((rectangle, source));
                    _ = target;
                    continue;
                }
                Fail<object>(recordOffset, $"template Rectangle holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (rectangles take simple values plus TemplateBinding values; style-surface follow-up)");
            }
        }

        /// <summary>Reads one PropertyComplexStart child inside a template
        /// element (fixed U16 attribute, no size prefix): the attribute
        /// resolves through the per-file table or the well-known template
        /// member ids, the single child element is a Binding, a
        /// RelativeSource, a TransformGroup or a TemplateBinding, and the
        /// child closes with PropertyComplexEnd. The staged #104 Number
        /// properties hold Bindings with a constructor Path plus a
        /// RelativeSource child; the staged LeftTemplate LayoutTransform
        /// holds a TransformGroup with a RotateTransform child; the staged
        /// BadgeDotTemplate Border Visibility holds a TemplateBinding with
        /// a constructor path plus a StaticResource Converter.</summary>
        private (DependencyProperty Property, object Value) ReadTemplateComplexChild(int offset, string entryDisplay, string setterDisplay, Type elementType)
        {
            if (_pos + 2 > bytes.Length)
                Fail<object>(offset, $"truncated BAML complex property on setter '{setterDisplay}' in style entry {entryDisplay}");
            var attrId = ReadI16();
            var (property, display) = ReadTemplateProperty(offset, entryDisplay, setterDisplay, elementType, attrId);
            var childRecord = ReadEntryRecord(out var childOffset);
            if (childRecord != 0x03)
                Fail<object>(childOffset, $"template complex property '{display}' on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{childRecord:X2} (complex properties take one element; style-surface follow-up)");
            var childType = ReadI16();
            _pos += 1; // flags byte (skipped like ElementStart)
            object value;
            if (childType == KnownBinding) value = ParseTemplateBinding(childOffset, entryDisplay, setterDisplay);
            else if (childType == KnownTemplateBinding) value = ParseTemplateTemplateBinding(childOffset, entryDisplay, setterDisplay);
            else if (childType == KnownRelativeSource) value = ParseTemplateRelativeSource(childOffset, entryDisplay, setterDisplay);
            else if (childType == KnownTransformGroup) value = ParseTemplateTransformGroup(childOffset, entryDisplay, setterDisplay);
            else
            {
                Fail<object>(childOffset, $"template complex property '{display}' on setter '{setterDisplay}' in style entry {entryDisplay} holds <{TargetTypeName(childType)}> (complex children take a Binding, RelativeSource, TransformGroup or TemplateBinding; style-surface follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            var closeRecord = ReadEntryRecord(out var closeOffset);
            if (closeRecord != 0x08)
                Fail<object>(closeOffset, $"template complex property '{display}' on setter '{setterDisplay}' in style entry {entryDisplay} holds record 0x{closeRecord:X2} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
            return (property, value);
        }

        /// <summary>Parses a TransformGroup element inside a template
        /// complex property (its ElementStart is consumed): the Children
        /// content member hosts RotateTransform children (Angle simples
        /// through the DoubleConverter), closed by ElementEnd. The staged
        /// LeftTemplate LayoutTransform is this exact shape (one
        /// RotateTransform with Angle "-90").</summary>
        private TransformGroup ParseTemplateTransformGroup(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var group = new TransformGroup();
            var haveChildren = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return group;
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownTransformChildren)
                        Fail<object>(recordOffset, $"content member {member} on the template TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (groups take the Children content; style-surface follow-up)");
                    if (haveChildren)
                        Fail<object>(recordOffset, $"duplicate Children content on the template TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                    haveChildren = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveChildren)
                        Fail<object>(recordOffset, $"rotation child precedes the Children content on the template TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (BAML emits Children first; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownRotateTransform)
                        Fail<object>(recordOffset, $"element {TargetTypeName(childType)} in the TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (groups take RotateTransform children; style-surface follow-up)");
                    group.Children.Add(ParseTemplateRotateTransform(recordOffset, entryDisplay, setterDisplay));
                    continue;
                }
                Fail<object>(recordOffset, $"template TransformGroup holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (groups take the Children content plus RotateTransform children; style-surface follow-up)");
            }
        }

        /// <summary>Parses a RotateTransform inside a template TransformGroup
        /// (its ElementStart is consumed): Angle simples convert through the
        /// DoubleConverter, closed by ElementEnd. The staged LeftTemplate
        /// rotation carries Angle "-90".</summary>
        private RotateTransform ParseTemplateRotateTransform(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var rotate = new RotateTransform();
            var haveAngle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return rotate;
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (!IsRotateAngleAttribute(attrId))
                        Fail<object>(recordOffset, $"template RotateTransform property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (rotates take Angle; style-surface follow-up)");
                    if (conv != ConvDouble)
                        Fail<object>(recordOffset, $"template RotateTransform Angle '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (angles take the DoubleConverter; style-surface follow-up)");
                    if (haveAngle)
                        Fail<object>(recordOffset, $"duplicate Angle on the template RotateTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                    if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var angle))
                        Fail<object>(recordOffset, $"'{text}' is not a Double for Angle on setter '{setterDisplay}' in style entry {entryDisplay}");
                    rotate.Angle = angle;
                    haveAngle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"template RotateTransform holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (rotates take Angle; style-surface follow-up)");
            }
        }
        /// <summary>True for a per-file value attribute on a setter
        /// TransformGroup child (the AttributeInfo row carries the
        /// well-known child owner plus the property name, like the
        /// template RotateTransform Angle arm).</summary>
        private bool IsStyleTransformAttribute(short attrId, short ownerId, string name)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (owner, attrName) = _attrs[attrId];
            return owner == ownerId && string.Equals(attrName, name, StringComparison.Ordinal);
        }

        /// <summary>Reads one DoubleConverter simple on a setter
        /// TransformGroup child (ScaleX/ScaleY, AngleX/AngleY, Angle or
        /// X/Y through the DoubleConverter like the staged shapes).
        /// </summary>
        private double ReadStyleTransformDouble(int recordOffset, string entryDisplay, string setterDisplay, string child, string property, string text, short conv)
        {
            if (conv != ConvDouble)
                Fail<object>(recordOffset, $"setter {child} {property} '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (transform doubles take the DoubleConverter; style-surface follow-up)");
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return Fail<double>(recordOffset, $"'{text}' is not a Double for {property} on setter '{setterDisplay}' in style entry {entryDisplay}");
            return value;
        }

        /// <summary>Parses a ScaleTransform inside a setter TransformGroup
        /// (its ElementStart is consumed): ScaleX/ScaleY DoubleConverter
        /// simples, closed by ElementEnd. The staged RightPathStyle group
        /// carries ScaleY "1" plus ScaleX "-1".</summary>
        private ScaleTransform ParseStyleScaleTransform(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var scale = new ScaleTransform();
            var haveX = false;
            var haveY = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveX)
                        Fail<object>(recordOffset, $"setter ScaleTransform on setter '{setterDisplay}' in style entry {entryDisplay} has no ScaleX (scales take ScaleX plus ScaleY; style-surface follow-up)");
                    if (!haveY)
                        Fail<object>(recordOffset, $"setter ScaleTransform on setter '{setterDisplay}' in style entry {entryDisplay} has no ScaleY (scales take ScaleX plus ScaleY; style-surface follow-up)");
                    return scale;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (IsStyleTransformAttribute(attrId, KnownScaleTransform, "ScaleX"))
                    {
                        if (haveX)
                            Fail<object>(recordOffset, $"duplicate ScaleX on the setter ScaleTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        scale.ScaleX = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "ScaleTransform", "ScaleX", text, conv);
                        haveX = true;
                        continue;
                    }
                    if (IsStyleTransformAttribute(attrId, KnownScaleTransform, "ScaleY"))
                    {
                        if (haveY)
                            Fail<object>(recordOffset, $"duplicate ScaleY on the setter ScaleTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        scale.ScaleY = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "ScaleTransform", "ScaleY", text, conv);
                        haveY = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"setter ScaleTransform property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (scales take ScaleX/ScaleY; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"setter ScaleTransform holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (scales take ScaleX/ScaleY; style-surface follow-up)");
            }
        }

        /// <summary>Parses a SkewTransform inside a setter TransformGroup
        /// (its ElementStart is consumed): AngleX/AngleY DoubleConverter
        /// simples, closed by ElementEnd. The staged RightPathStyle group
        /// carries AngleY "0" plus AngleX "0".</summary>
        private SkewTransform ParseStyleSkewTransform(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var skew = new SkewTransform();
            var haveX = false;
            var haveY = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveX)
                        Fail<object>(recordOffset, $"setter SkewTransform on setter '{setterDisplay}' in style entry {entryDisplay} has no AngleX (skews take AngleX plus AngleY; style-surface follow-up)");
                    if (!haveY)
                        Fail<object>(recordOffset, $"setter SkewTransform on setter '{setterDisplay}' in style entry {entryDisplay} has no AngleY (skews take AngleX plus AngleY; style-surface follow-up)");
                    return skew;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (IsStyleTransformAttribute(attrId, KnownSkewTransform, "AngleX"))
                    {
                        if (haveX)
                            Fail<object>(recordOffset, $"duplicate AngleX on the setter SkewTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        skew.AngleX = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "SkewTransform", "AngleX", text, conv);
                        haveX = true;
                        continue;
                    }
                    if (IsStyleTransformAttribute(attrId, KnownSkewTransform, "AngleY"))
                    {
                        if (haveY)
                            Fail<object>(recordOffset, $"duplicate AngleY on the setter SkewTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        skew.AngleY = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "SkewTransform", "AngleY", text, conv);
                        haveY = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"setter SkewTransform property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (skews take AngleX/AngleY; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"setter SkewTransform holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (skews take AngleX/AngleY; style-surface follow-up)");
            }
        }

        /// <summary>Parses a RotateTransform inside a setter TransformGroup
        /// (its ElementStart is consumed): the Angle DoubleConverter simple,
        /// closed by ElementEnd. The staged RightPathStyle group carries
        /// Angle "0".</summary>
        private RotateTransform ParseStyleRotateTransform(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var rotate = new RotateTransform();
            var haveAngle = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveAngle)
                        Fail<object>(recordOffset, $"setter RotateTransform on setter '{setterDisplay}' in style entry {entryDisplay} has no Angle (rotates take Angle; style-surface follow-up)");
                    return rotate;
                }
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (!IsStyleTransformAttribute(attrId, KnownRotateTransform, "Angle"))
                        Fail<object>(recordOffset, $"setter RotateTransform property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (rotates take Angle; style-surface follow-up)");
                    if (haveAngle)
                        Fail<object>(recordOffset, $"duplicate Angle on the setter RotateTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                    rotate.Angle = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "RotateTransform", "Angle", text, conv);
                    haveAngle = true;
                    continue;
                }
                Fail<object>(recordOffset, $"setter RotateTransform holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (rotates take Angle; style-surface follow-up)");
            }
        }

        /// <summary>Parses a TranslateTransform inside a setter TransformGroup
        /// (its ElementStart is consumed): the optional X/Y DoubleConverter
        /// simples, closed by ElementEnd. The staged RightPathStyle group
        /// carries an empty TranslateTransform (X/Y default 0).</summary>
        private TranslateTransform ParseStyleTranslateTransform(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var translate = new TranslateTransform();
            var haveX = false;
            var haveY = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04) return translate;
                if (record == 0x24)
                {
                    var (attrId, text, conv) = ReadConvertedPropertyPayload(recordOffset);
                    if (IsStyleTransformAttribute(attrId, KnownTranslateTransform, "X"))
                    {
                        if (haveX)
                            Fail<object>(recordOffset, $"duplicate X on the setter TranslateTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        translate.X = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "TranslateTransform", "X", text, conv);
                        haveX = true;
                        continue;
                    }
                    if (IsStyleTransformAttribute(attrId, KnownTranslateTransform, "Y"))
                    {
                        if (haveY)
                            Fail<object>(recordOffset, $"duplicate Y on the setter TranslateTransform on setter '{setterDisplay}' in style entry {entryDisplay}");
                        translate.Y = ReadStyleTransformDouble(recordOffset, entryDisplay, setterDisplay, "TranslateTransform", "Y", text, conv);
                        haveY = true;
                        continue;
                    }
                    Fail<object>(recordOffset, $"setter TranslateTransform property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (translates take X/Y; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"setter TranslateTransform holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (translates take X/Y; style-surface follow-up)");
            }
        }

        /// <summary>Parses a TransformGroup value on a style Setter (its
        /// ElementStart is consumed): the Children content member plus
        /// ScaleTransform/SkewTransform/RotateTransform/TranslateTransform
        /// children with DoubleConverter simples, closed by ElementEnd. The
        /// staged RightPathStyle RenderTransform setter is this exact shape
        /// (ScaleY "1"/ScaleX "-1", AngleY/AngleX "0", Angle "0", empty
        /// TranslateTransform); the group lands as the Setter value like
        /// the XAML loader path.</summary>
        private TransformGroup ParseStyleTransformGroup(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var group = new TransformGroup();
            var haveChildren = false;
            var haveScale = false;
            var haveSkew = false;
            var haveRotate = false;
            var haveTranslate = false;
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x04)
                {
                    if (!haveChildren)
                        Fail<object>(recordOffset, $"setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} has no Children (groups take the Children content plus transform children; style-surface follow-up)");
                    return group;
                }
                if (record == 0x2E)
                {
                    var member = ReadI16();
                    if (member != KnownTransformChildren)
                        Fail<object>(recordOffset, $"content member {member} on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (groups take the Children content; style-surface follow-up)");
                    if (haveChildren)
                        Fail<object>(recordOffset, $"duplicate Children content on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                    haveChildren = true;
                    continue;
                }
                if (record == 0x03)
                {
                    if (!haveChildren)
                        Fail<object>(recordOffset, $"transform child precedes the Children content on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (BAML emits Children first; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType == KnownScaleTransform)
                    {
                        if (haveScale)
                            Fail<object>(recordOffset, $"duplicate ScaleTransform on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                        haveScale = true;
                        group.Children.Add(ParseStyleScaleTransform(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    if (childType == KnownSkewTransform)
                    {
                        if (haveSkew)
                            Fail<object>(recordOffset, $"duplicate SkewTransform on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                        haveSkew = true;
                        group.Children.Add(ParseStyleSkewTransform(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    if (childType == KnownRotateTransform)
                    {
                        if (haveRotate)
                            Fail<object>(recordOffset, $"duplicate RotateTransform on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                        haveRotate = true;
                        group.Children.Add(ParseStyleRotateTransform(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    if (childType == KnownTranslateTransform)
                    {
                        if (haveTranslate)
                            Fail<object>(recordOffset, $"duplicate TranslateTransform on the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay}");
                        haveTranslate = true;
                        group.Children.Add(ParseStyleTranslateTransform(recordOffset, entryDisplay, setterDisplay));
                        continue;
                    }
                    Fail<object>(recordOffset, $"element {TargetTypeName(childType)} in the setter TransformGroup on setter '{setterDisplay}' in style entry {entryDisplay} (groups take ScaleTransform/SkewTransform/RotateTransform/TranslateTransform children; style-surface follow-up)");
                    continue;
                }
                Fail<object>(recordOffset, $"setter TransformGroup holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (groups take the Children content plus transform children; style-surface follow-up)");
            }
        }

        /// <summary>Parses a Binding element inside a template (its
        /// ElementStart is consumed): an optional constructor-parameters
        /// group carries the Path text (the staged GroupBox template also
        /// carries the Path as a PropertyWithConverter value through the
        /// per-file Binding.Path row, e.g. "(hc:TitleElement.MinHeight)"
        /// with the PropertyPathConverter), an optional RelativeSource
        /// complex child carries the mode, an optional StaticResource
        /// Converter value (0x38 over the per-file Binding.Converter row,
        /// indexing the entry's key-block StaticResource declarations, e.g.
        /// the staged GroupBoxTabTopTemplate CornerRadius/Thickness
        /// converters) lands as the deferred converter key like the XAML
        /// loader path, and an optional ConverterParameter simple value
        /// lands verbatim. Anything else is a precise follow-up. Closed by
        /// ElementEnd.</summary>
        private Binding ParseTemplateBinding(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var path = string.Empty;
            var havePath = false;
            string? converterKey = null;
            string? converterParameter = null;
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the template Binding");
                if (args.Count > 1)
                    Fail<object>(recordOffset, $"template Binding on setter '{setterDisplay}' in style entry {entryDisplay} takes one constructor argument (the path; richer markup-extension arguments are a style-surface follow-up)");
                if (args.Count == 1) { path = args[0]; havePath = true; }
                record = ReadEntryRecord(out recordOffset);
            }
            var binding = new Binding(path);
            while (true)
            {
                if (record == 0x04)
                {
                    binding.Path = new PropertyPath(path);
                    if (converterKey is not null) binding.DeferredConverterKey = converterKey;
                    if (converterParameter is not null) binding.ConverterParameter = converterParameter;
                    return binding;
                }
                if (record == 0x38)
                {
                    if (_pos + 4 > bytes.Length)
                        Fail<object>(recordOffset, $"truncated BAML StaticResource value on the template Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    var staticAttr = ReadI16();
                    var staticId = (int)ReadU16();
                    if (!IsBindingConverterAttribute(staticAttr))
                        Fail<object>(recordOffset, $"template Binding holds a StaticResource value for {BindingAttributeDisplay(staticAttr)} on setter '{setterDisplay}' in style entry {entryDisplay} (bindings take the Converter plus ConverterParameter; style-surface follow-up)");
                    if (converterKey is not null)
                        Fail<object>(recordOffset, $"duplicate Converter on the template Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    if (_entryStatics is null || _entryStatics.Count == 0)
                        Fail<object>(recordOffset, $"template Binding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names static resource {staticId} with no StaticResource index (bindings take indexed static values; style-surface follow-up)");
                    if (staticId >= _entryStatics!.Count)
                        Fail<object>(recordOffset, $"template Binding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names static resource {staticId} (only {_entryStatics!.Count} indexed static values; style-surface follow-up)");
                    var (flags, id, key) = _entryStatics![staticId];
                    _ = id;
                    if (flags != 0 || string.IsNullOrEmpty(key))
                        Fail<object>(recordOffset, $"template Binding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names a non-string static resource (bindings take string-keyed converters; style-surface follow-up)");
                    converterKey = key;
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                if (record == 0x07)
                {
                    if (_pos + 2 > bytes.Length)
                        Fail<object>(recordOffset, $"truncated BAML Binding property on setter '{setterDisplay}' in style entry {entryDisplay}");
                    var attrId = ReadI16();
                    if (!IsRelativeSourceAttribute(attrId))
                        Fail<object>(recordOffset, $"template Binding holds complex property {TemplateAttributeDisplay(attrId)} on setter '{setterDisplay}' in style entry {entryDisplay} (bindings take RelativeSource; style-surface follow-up)");
                    var childRecord = ReadEntryRecord(out var childOffset);
                    if (childRecord != 0x03)
                        Fail<object>(childOffset, $"template Binding holds record 0x{childRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (RelativeSource takes one element; style-surface follow-up)");
                    var childType = ReadI16();
                    _pos += 1; // flags byte (skipped like ElementStart)
                    if (childType != KnownRelativeSource)
                        Fail<object>(childOffset, $"template Binding holds <{TargetTypeName(childType)}> on setter '{setterDisplay}' in style entry {entryDisplay} (RelativeSource takes a RelativeSource child; style-surface follow-up)");
                    if (binding.RelativeSource is not null)
                        Fail<object>(childOffset, $"duplicate RelativeSource on the template Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    binding.RelativeSource = ParseTemplateRelativeSource(childOffset, entryDisplay, setterDisplay);
                    var closeRecord = ReadEntryRecord(out var closeOffset);
                    if (closeRecord != 0x08)
                        Fail<object>(closeOffset, $"template Binding holds record 0x{closeRecord:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (complex properties close with PropertyComplexEnd; style-surface follow-up)");
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                if (record == 0x24)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    var conv = ReadI16();
                    ExpectEnd(end, recordOffset, "PropertyWithConverter");
                    if (IsBindingConverterParameterAttribute(attrId))
                    {
                        if (conv != ConvString)
                            Fail<object>(recordOffset, $"template Binding ConverterParameter '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (Binding parameters take the StringConverter; style-surface follow-up)");
                        if (converterParameter is not null)
                            Fail<object>(recordOffset, $"duplicate ConverterParameter on the template Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                        converterParameter = text;
                        record = ReadEntryRecord(out recordOffset);
                        continue;
                    }
                    if (!IsBindingPathAttribute(attrId))
                        Fail<object>(recordOffset, $"template Binding holds a simple value for {TemplateAttributeDisplay(attrId)} on setter '{setterDisplay}' in style entry {entryDisplay} (bindings take the Path plus Converter plus ConverterParameter plus RelativeSource; style-surface follow-up)");
                    if (conv != ConvPropertyPath)
                        Fail<object>(recordOffset, $"template Binding Path '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} uses converter {conv} (Binding paths take the PropertyPathConverter; style-surface follow-up)");
                    if (havePath)
                        Fail<object>(recordOffset, $"duplicate Path on the template Binding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    path = text;
                    havePath = true;
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                Fail<object>(recordOffset, $"template Binding holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (bindings take a path plus Converter plus ConverterParameter plus RelativeSource; style-surface follow-up)");
            }
        }

        /// <summary>Parses a TemplateBinding element inside a template
        /// complex property (its ElementStart is consumed): the required
        /// constructor-parameters group carries the templated-parent path
        /// text (the staged BadgeDotTemplate Visibility binds ShowBadge),
        /// an optional StaticResource Converter value (0x38 over the
        /// per-file TemplateBinding-owned Converter row, indexing the
        /// entry's key-block StaticResource declarations like template
        /// Bindings, e.g. the staged Boolean2VisibilityConverter) lands as
        /// the deferred converter key like the XAML loader path, and
        /// ElementEnd closes. Anything else is a precise follow-up. The
        /// transfer lands as a TemplatedParent-sourced Binding (a
        /// TemplateBinding is WPF's short-form TemplatedParent binding),
        /// inert until BAML-tree inflation lands.</summary>
        private Binding ParseTemplateTemplateBinding(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var path = string.Empty;
            var havePath = false;
            string? converterKey = null;
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the template TemplateBinding");
                if (args.Count > 1)
                    Fail<object>(recordOffset, $"template TemplateBinding on setter '{setterDisplay}' in style entry {entryDisplay} takes one constructor argument (the path; richer markup-extension arguments are a style-surface follow-up)");
                if (args.Count == 1) { path = args[0]; havePath = true; }
                record = ReadEntryRecord(out recordOffset);
            }
            if (!havePath)
                Fail<object>(recordOffset, $"template TemplateBinding on setter '{setterDisplay}' in style entry {entryDisplay} has no path (template bindings take the templated-parent path; style-surface follow-up)");
            var binding = new Binding(path) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
            while (true)
            {
                if (record == 0x04)
                {
                    binding.Path = new PropertyPath(path);
                    if (converterKey is not null) binding.DeferredConverterKey = converterKey;
                    return binding;
                }
                if (record == 0x38)
                {
                    if (_pos + 4 > bytes.Length)
                        Fail<object>(recordOffset, $"truncated BAML StaticResource value on the template TemplateBinding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    var staticAttr = ReadI16();
                    var staticId = (int)ReadU16();
                    if (!IsTemplateBindingConverterAttribute(staticAttr))
                        Fail<object>(recordOffset, $"template TemplateBinding holds a StaticResource value for {BindingAttributeDisplay(staticAttr)} on setter '{setterDisplay}' in style entry {entryDisplay} (template bindings take the Converter; style-surface follow-up)");
                    if (converterKey is not null)
                        Fail<object>(recordOffset, $"duplicate Converter on the template TemplateBinding on setter '{setterDisplay}' in style entry {entryDisplay}");
                    if (_entryStatics is null || _entryStatics.Count == 0)
                        Fail<object>(recordOffset, $"template TemplateBinding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names static resource {staticId} with no StaticResource index (template bindings take indexed static values; style-surface follow-up)");
                    if (staticId >= _entryStatics!.Count)
                        Fail<object>(recordOffset, $"template TemplateBinding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names static resource {staticId} (only {_entryStatics!.Count} indexed static values; style-surface follow-up)");
                    var (flags, id, key) = _entryStatics![staticId];
                    _ = id;
                    if (flags != 0 || string.IsNullOrEmpty(key))
                        Fail<object>(recordOffset, $"template TemplateBinding Converter on setter '{setterDisplay}' in style entry {entryDisplay} names a non-string static resource (template bindings take string-keyed converters; style-surface follow-up)");
                    converterKey = key;
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                Fail<object>(recordOffset, $"template TemplateBinding holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (template bindings take a path plus Converter; style-surface follow-up)");
            }
        }

        /// <summary>Parses a RelativeSource element inside a template Binding
        /// (its ElementStart is consumed): an optional constructor-parameters
        /// group carries the mode name (absent means FindAncestor), plus an
        /// optional AncestorType Property simple (the staged CarouselItemBaseStyle
        /// ContentTemplate Binding carries AncestorType="hc:Carousel" this way,
        /// byte-verified against a genuine net10.0-windows fixture compiling the
        /// same RelativeSource shape with framework-only types). The type text
        /// resolves through the file xmlns/namespace-mapping tables to a loaded
        /// converted type (prefix:Type) or an engine short name. Closed by
        /// ElementEnd.</summary>
        private RelativeSource ParseTemplateRelativeSource(int offset, string entryDisplay, string setterDisplay)
        {
            _ = offset;
            var mode = RelativeSourceMode.FindAncestor;
            Type? ancestorType = null;
            var record = ReadEntryRecord(out var recordOffset);
            if (record == 0x2A)
            {
                var args = ReadCtorParams(recordOffset, "the template RelativeSource");
                if (args.Count != 1)
                    Fail<object>(recordOffset, $"template RelativeSource on setter '{setterDisplay}' in style entry {entryDisplay} takes one constructor argument (the mode; richer markup-extension arguments are a style-surface follow-up)");
                try { mode = Enum.Parse<RelativeSourceMode>(args[0], ignoreCase: false); }
                catch (ArgumentException)
                {
                    return Fail<RelativeSource>(recordOffset, $"template RelativeSource names '{args[0]}' on setter '{setterDisplay}' in style entry {entryDisplay} (relative-source modes take FindAncestor/Self/TemplatedParent/PreviousData; style-surface follow-up)");
                }
                record = ReadEntryRecord(out recordOffset);
            }
            while (true)
            {
                if (record == 0x04)
                {
                    var source = new RelativeSource(mode);
                    if (ancestorType is not null) source.AncestorType = ancestorType;
                    return source;
                }
                if (record == 0x05)
                {
                    var end = StartSized(out _);
                    var attrId = ReadI16();
                    var text = ReadAnsiString();
                    ExpectEnd(end, recordOffset, "Property");
                    if (!IsRelativeSourceAncestorTypeAttribute(attrId))
                        Fail<object>(recordOffset, $"template RelativeSource holds attribute {BindingAttributeDisplay(attrId)} on setter '{setterDisplay}' in style entry {entryDisplay} (relative sources take AncestorType; style-surface follow-up)");
                    if (ancestorType is not null)
                        Fail<object>(recordOffset, $"duplicate AncestorType on the template RelativeSource on setter '{setterDisplay}' in style entry {entryDisplay}");
                    ancestorType = ResolveAncestorType(recordOffset, entryDisplay, setterDisplay, text);
                    record = ReadEntryRecord(out recordOffset);
                    continue;
                }
                Fail<object>(recordOffset, $"template RelativeSource holds record 0x{record:X2} on setter '{setterDisplay}' in style entry {entryDisplay} (relative sources take a mode plus AncestorType; style-surface follow-up)");
            }
        }

        /// <summary>True for the AncestorType value attribute on a template
        /// RelativeSource (per-file AttributeInfo row owned by RelativeSource,
        /// e.g. the staged CarouselItemBaseStyle row plus the genuine net10
        /// fixture row).</summary>
        private bool IsRelativeSourceAncestorTypeAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownRelativeSource && string.Equals(name, "AncestorType", StringComparison.Ordinal);
        }

        /// <summary>Resolves template RelativeSource AncestorType text to a
        /// loaded type: prefix:Type travels through the file xmlns table
        /// (prefix to xmlns) and namespace-mapping table (xmlns to
        /// clr-namespace plus assembly) to a type in a loaded assembly with
        /// that short name (the staged 'hc:Carousel' names the converted
        /// HandyControl.Controls.Carousel), while a bare name resolves an
        /// engine short name (synthetic fixtures). Anything else fails precise
        /// so a half-built binding never lands.</summary>
        private Type ResolveAncestorType(int recordOffset, string entryDisplay, string setterDisplay, string text)
        {
            var colon = text.IndexOf(':');
            if (colon <= 0 || colon == text.Length - 1)
            {
                var engine = XamlLoader.FindEngineType(text);
                if (engine is not null) return engine;
                return Fail<Type>(recordOffset, $"template RelativeSource AncestorType '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} (relative-source ancestor types take prefix:Type or an engine type; style-surface follow-up)");
            }
            var prefix = text[..colon];
            var localName = text[(colon + 1)..];
            string? xmlns = null;
            foreach (var (candidatePrefix, candidateXmlns, _) in _xmlns)
            {
                if (string.Equals(candidatePrefix, prefix, StringComparison.Ordinal)) { xmlns = candidateXmlns; break; }
            }
            if (xmlns is null)
                return Fail<Type>(recordOffset, $"template RelativeSource AncestorType '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} names unknown xmlns prefix '{prefix}' (relative-source ancestor types take a mapped prefix; style-surface follow-up)");
            string? clrNamespace = null;
            var assemblyId = -1;
            foreach (var (candidateXmlns, candidateClr, candidateAsm) in _piMapping)
            {
                if (string.Equals(candidateXmlns, xmlns, StringComparison.Ordinal)) { clrNamespace = candidateClr; assemblyId = candidateAsm; break; }
            }
            if (clrNamespace is null)
                return Fail<Type>(recordOffset, $"template RelativeSource AncestorType '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} has no namespace mapping for '{xmlns}' (relative-source ancestor types take a mapped namespace; style-surface follow-up)");
            if (assemblyId < 0 || assemblyId >= _assemblies.Count)
                return Fail<Type>(recordOffset, $"template RelativeSource AncestorType '{text}' on setter '{setterDisplay}' in style entry {entryDisplay} references assembly id {assemblyId} (only {_assemblies.Count} AssemblyInfo records)");
            var full = _assemblies[assemblyId];
            var comma = full.IndexOf(',');
            var shortName = comma < 0 ? full.Trim() : full[..comma].Trim();
            var fullName = clrNamespace + "." + localName;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, shortName, StringComparison.OrdinalIgnoreCase)) continue;
                if (SafeGetType(assembly, fullName) is { } candidate) return candidate;
            }
            return Fail<Type>(recordOffset, $"'{text}' on setter '{setterDisplay}' in style entry {entryDisplay} names '{fullName}' (no loadable converted type with that full name in assembly '{shortName}')");
        }

        /// <summary>Reads a constructor-parameters group (bare 0x2A start,
        /// Text values, bare 0x2B end) inside a template markup element: the
        /// staged Binding Path and RelativeSource Mode travel this way,
        /// byte-verified against the real net10 BAML fixture. Non-text
        /// arguments are a precise follow-up.</summary>
        private List<string> ReadCtorParams(int offset, string what)
        {
            _ = offset;
            var args = new List<string>();
            while (true)
            {
                var record = ReadEntryRecord(out var recordOffset);
                if (record == 0x2B) return args;
                if (record == 0x10)
                {
                    args.Add(ReadSizedString(recordOffset, "constructor text"));
                    continue;
                }
                Fail<object>(recordOffset, $"constructor parameters on {what} hold record 0x{record:X2} (markup-extension constructor arguments take text; style-surface follow-up)");
            }
        }

        /// <summary>Resolves a template property attribute to a dependency
        /// property: non-negative ids index the per-file AttributeInfo table
        /// (the owner resolves like setter-property owners: well-known
        /// framework owners plus engine short names and converted full
        /// names), negative ids are well-known template members resolved
        /// against the element type itself.</summary>
        private (DependencyProperty Property, string Display) ReadTemplateProperty(int offset, string entryDisplay, string setterDisplay, Type elementType, short attrId)
        {
            if (attrId >= 0)
            {
                if (attrId >= _attrs.Count)
                    Fail<object>(offset, $"template property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} has no AttributeInfo record");
                var (ownerId, name) = _attrs[attrId];
                var owner = ResolveTemplateAttrOwner(offset, entryDisplay, setterDisplay, ownerId);
                var dp = DependencyProperty.FromName(owner, name);
                if (dp is null)
                    Fail<object>(offset, $"'{name}' is not a dependency property of {owner.Name} on setter '{setterDisplay}' in style entry {entryDisplay}");
                return (dp!, owner.Name + "." + name);
            }
            var memberName = TemplateMemberName(attrId);
            if (memberName is null)
                Fail<object>(offset, $"template property id {attrId} on setter '{setterDisplay}' in style entry {entryDisplay} (template members beyond Margin/Text/TextTrimming/StrokeThickness/Height/Width/MinHeight/MinWidth/Background/BorderThickness/Visibility/HorizontalAlignment/VerticalAlignment/LastChildFill/ContentTemplate/HorizontalScrollBarVisibility/VerticalScrollBarVisibility are a style-surface follow-up)");
            var elementDp = DependencyProperty.FromName(elementType, memberName);
            if (elementDp is null)
                Fail<object>(offset, $"'{memberName}' is not a dependency property of {elementType.Name} on setter '{setterDisplay}' in style entry {entryDisplay}");
            return (elementDp!, memberName);
        }

        /// <summary>Resolves a template attribute owner: the verified
        /// well-known framework owners plus per-file TypeInfo rows (engine
        /// short names and converted full names, never instantiated here).
        /// Anything else fails precise.</summary>
        private Type ResolveTemplateAttrOwner(int offset, string entryDisplay, string setterDisplay, short ownerId)
        {
            if (ownerId == KnownStackPanel) return typeof(StackPanel);
            if (ownerId == KnownVirtualizingStackPanel) return typeof(VirtualizingStackPanel);
            if (ownerId == KnownWrapPanel) return typeof(WrapPanel);
            if (ownerId == KnownBinding) return typeof(Binding);
            if (ownerId == KnownBorder) return typeof(Border);
            if (ownerId == KnownShape) return typeof(Shape);
            if (ownerId == KnownRectangle) return typeof(Rectangle);
            if (ownerId == KnownUIElement) return typeof(UIElement);
            if (ownerId == KnownFrameworkElement) return typeof(FrameworkElement);
            if (ownerId < 0)
                Fail<object>(offset, $"template attribute owner {WellKnownName(ownerId)} on setter '{setterDisplay}' in style entry {entryDisplay} (template attribute owners beyond StackPanel/VirtualizingStackPanel/WrapPanel/Binding/Border/Shape/Rectangle/UIElement/FrameworkElement are a style-surface follow-up)");
            if (ownerId >= _types.Count)
                Fail<object>(offset, $"template attribute owner id {ownerId} on setter '{setterDisplay}' in style entry {entryDisplay} (only {_types.Count} TypeInfo records)");
            var fullName = TypeInfoName(ownerId);
            var shortName = fullName[(fullName.LastIndexOf('.') + 1)..];
            var engine = XamlLoader.FindEngineType(shortName);
            if (engine is not null) return engine;
            var converted = ResolveConvertedOwnerType(offset, entryDisplay, fullName);
            if (converted is not null) return converted;
            Fail<object>(offset, $"template attribute owner '{fullName}' on setter '{setterDisplay}' in style entry {entryDisplay} is a converted type (style-surface follow-up)");
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>Well-known template member names (the attribute table for
        /// PropertyWithConverter/PropertyComplexStart inside template
        /// visuals, verified against the real net10 BAML fixture): Margin/
        /// Text/TextTrimming plus the staged RepeatButton template members
        /// (StrokeThickness on the Rectangle, Height/Width on
        /// Border/Rectangle visuals) plus the staged GroupBox template
        /// members (MinHeight/MinWidth/Background/BorderThickness complex
        /// bindings on the Border, e.g. the staged GroupBoxTabTopTemplate
        /// BorderThickness TemplatedParent binding with a StaticResource
        /// converter) plus Visibility (the staged CircleProgressBar
        /// TextBlock binds Visibility to the templated parent) plus
        /// HorizontalAlignment/VerticalAlignment/LastChildFill (the staged Wrap/Dock items-panel visuals carry
        /// them as PropertyWithConverter simples) plus ContentTemplate (the staged
        /// CarouselItemBaseStyle ContentPresenter binds ContentTemplate to the
        /// templated item through an AncestorType Carousel Binding, verified
        /// against a genuine net10.0-windows fixture compiling the same
        /// ContentTemplate/RelativeSource shape with framework-only types) plus HorizontalScrollBarVisibility/VerticalScrollBarVisibility (the staged RibbonTab ScrollViewer carries Hidden/Disabled through the EnumConverter, verified against a genuine net10.0-windows fixture compiling the same ScrollViewer/ItemsPresenter block).</summary>
        private static string? TemplateMemberName(short attrId) => attrId switch
        {
            KnownMarginAttribute => "Margin",
            KnownTextAttribute => "Text",
            KnownTextTrimmingMember => "TextTrimming",
            KnownContentTemplateMember => "ContentTemplate",
            KnownStrokeThicknessMember => "StrokeThickness",
            KnownHeightMember => "Height",
            KnownWidthMember => "Width",
            KnownMinHeightMember => "MinHeight",
            KnownMinWidthMember => "MinWidth",
            KnownBorderBackgroundMember => "Background",
            KnownBorderThicknessMember => "BorderThickness",
            KnownVisibilityMember => "Visibility",
            KnownHAlignMember => "HorizontalAlignment",
            KnownVAlignMember => "VerticalAlignment",
            KnownLastChildFill => "LastChildFill",
            KnownHScrollBarVisibilityMember => "HorizontalScrollBarVisibility",
            KnownVScrollBarVisibilityMember => "VerticalScrollBarVisibility",
            _ => null,
        };

        /// <summary>Display for a template Binding complex attribute.</summary>
        private static string TemplateAttributeDisplay(short attrId) => attrId switch
        {
            _ when attrId >= 0 => $"id {attrId}",
            _ => TemplateMemberName(attrId) is { } name ? $"'{name}'" : $"id {attrId}",
        };

        /// <summary>True for the Path value attribute on a template
        /// Binding (per-file AttributeInfo row owned by Binding).</summary>
        private bool IsBindingPathAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownBinding && string.Equals(name, "Path", StringComparison.Ordinal);
        }

        /// <summary>True for the Converter value attribute on a template
        /// Binding (per-file AttributeInfo row owned by Binding, e.g. the
        /// staged GroupBoxTabTopTemplate row 37).</summary>
        private bool IsBindingConverterAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownBinding && string.Equals(name, "Converter", StringComparison.Ordinal);
        }

        /// <summary>True for the ConverterParameter value attribute on a
        /// template Binding (per-file AttributeInfo row owned by Binding,
        /// e.g. the staged GroupBoxTabTopTemplate row 38).</summary>
        /// <summary>True for the Converter value attribute on a template
        /// TemplateBinding (per-file AttributeInfo row owned by
        /// TemplateBindingExtension, e.g. the staged BadgeDotTemplate row
        /// 56).</summary>
        private bool IsTemplateBindingConverterAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownTemplateBinding && string.Equals(name, "Converter", StringComparison.Ordinal);
        }

        private bool IsBindingConverterParameterAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownBinding && string.Equals(name, "ConverterParameter", StringComparison.Ordinal);
        }

        /// <summary>Display for a template Binding value attribute (the
        /// per-file name when the row exists, otherwise the numeric id).</summary>
        private string BindingAttributeDisplay(short attrId)
        {
            if (attrId >= 0 && attrId < _attrs.Count && !string.IsNullOrEmpty(_attrs[attrId].Name))
                return $"'{_attrs[attrId].Name}'";
            return TemplateAttributeDisplay(attrId);
        }

        /// <summary>True for the RelativeSource complex attribute on a
        /// template Binding (per-file AttributeInfo row owned by Binding).</summary>
        private bool IsRelativeSourceAttribute(short attrId)
        {
            if (attrId < 0 || attrId >= _attrs.Count) return false;
            var (ownerId, name) = _attrs[attrId];
            return ownerId == KnownBinding && string.Equals(name, "RelativeSource", StringComparison.Ordinal);
        }

        /// <summary>Converts a template PropertyWithConverter text through
        /// the resolved dependency-property type (the same real load
        /// conversion style setters use). Converter ids pin the staged
        /// shapes: strings, bools, doubles, LengthConverter doubles, enums,
        /// Thickness, CornerRadius and Duration.</summary>
        private object ConvertTemplateValue(int offset, string entryDisplay, string setterDisplay, string display, string text, short converter, DependencyProperty property)
        {
            var target = property.PropertyType;
            if (converter == ConvString && target == typeof(string)) return text;
            if (converter == ConvBool && target == typeof(bool) && bool.TryParse(text, out var b)) return b;
            if (converter == ConvDouble && target == typeof(double)
                && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (converter == ConvLength && target == typeof(double)
                && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var length)) return length;
            if (converter == ConvEnum && target.IsEnum)
            {
                try { return Enum.Parse(target, text, ignoreCase: false); }
                catch (ArgumentException) { }
            }
            if (converter == ConvThickness && target == typeof(Thickness))
            {
                try { return Thickness.Parse(text); }
                catch (FormatException) { }
            }
            if (converter == ConvCornerRadius && target == typeof(CornerRadius))
            {
                try { return CornerRadius.Parse(text); }
                catch (FormatException) { }
            }
            if (converter == ConvDuration && target == typeof(Duration))
            {
                try { return Duration.Parse(text); }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { }
            }
            return Fail<object>(offset, $"'{text}' with converter {converter} does not convert to {target.Name} for '{display}' on setter '{setterDisplay}' in style entry {entryDisplay} (template simple values take strings/bools/doubles/LengthConverter/enums/Thickness/CornerRadius/Duration; style-surface follow-up)");
        }

        /// <summary>Reads a PropertyCustom dependency-property reference
        /// (sized I16 attribute + U16 serializer + payload). Plain 137
        /// carries a well-known member id; 0x4000-flagged 16521 carries an
        /// owner type id plus the property name. The attribute must be the
        /// Setter.Property row. Short-form members resolve against the style
        /// TargetType like WPF (Template walks to Control.TemplateProperty,
        /// even for converted targets deriving the engine Control; Focusable,
        /// IsEnabled, HorizontalAlignment, VerticalAlignment, Background,
        /// Height, Width, MaxWidth, Fill, Visibility, BorderThickness,
        /// BorderBrush, IsTabStop, Padding (and Background/Margin on Border)
        /// walk the hierarchy so
        /// converted targets land their inherited DPs).</summary>
        private (DependencyProperty Property, string Display) ReadSetterProperty(int offset, string display, Type? targetType)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var serializer = ReadU16();
            RequireAttribute(offset, display, attrId, KnownSetter, "Property");
            if (serializer == SerDependencyProperty)
            {
                var memberId = ReadI16();
                ExpectEnd(end, offset, "PropertyCustom");
                if (memberId == KnownDataMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'Data' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var data = DependencyProperty.FromName(targetType!, "Data");
                    if (data is null)
                        Fail<object>(offset, $"'Data' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (data!, "Data");
                }
                if (memberId == KnownFlowDirection)
                    return (FrameworkElement.FlowDirectionProperty, "FlowDirection");
                if (memberId == KnownTemplateMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'Template' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var template = DependencyProperty.FromName(targetType!, "Template");
                    if (template is null)
                        Fail<object>(offset, $"'Template' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (template!, "Template");
                }
                if (memberId == KnownFocusableMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'Focusable' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var focusable = DependencyProperty.FromName(targetType!, "Focusable");
                    if (focusable is null)
                        Fail<object>(offset, $"'Focusable' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (focusable!, "Focusable");
                }
                if (memberId == KnownIsEnabledMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'IsEnabled' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var enabled = DependencyProperty.FromName(targetType!, "IsEnabled");
                    if (enabled is null)
                        Fail<object>(offset, $"'IsEnabled' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (enabled!, "IsEnabled");
                }
                if (memberId == KnownHAlignMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'HorizontalAlignment' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var halign = DependencyProperty.FromName(targetType!, "HorizontalAlignment");
                    if (halign is null)
                        Fail<object>(offset, $"'HorizontalAlignment' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (halign!, "HorizontalAlignment");
                }
                if (memberId == KnownVAlignMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'VerticalAlignment' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var valign = DependencyProperty.FromName(targetType!, "VerticalAlignment");
                    if (valign is null)
                        Fail<object>(offset, $"'VerticalAlignment' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (valign!, "VerticalAlignment");
                }
                if (memberId == KnownTextTrimmingMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'TextTrimming' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var trimming = DependencyProperty.FromName(targetType!, "TextTrimming");
                    if (trimming is null)
                        Fail<object>(offset, $"'TextTrimming' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (trimming!, "TextTrimming");
                }
                if (memberId == KnownMaxWidthMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'MaxWidth' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var maxWidth = DependencyProperty.FromName(targetType!, "MaxWidth");
                    if (maxWidth is null)
                        Fail<object>(offset, $"'MaxWidth' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (maxWidth!, "MaxWidth");
                }
                if (memberId == KnownBorderBrushSourceMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'BorderBrush' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var borderBrush = DependencyProperty.FromName(targetType!, "BorderBrush");
                    if (borderBrush is null)
                        Fail<object>(offset, $"'BorderBrush' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (borderBrush!, "BorderBrush");
                }
                if (memberId == KnownBorderThicknessSourceMember)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter 'BorderThickness' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var borderThickness = DependencyProperty.FromName(targetType!, "BorderThickness");
                    if (borderThickness is null)
                        Fail<object>(offset, $"'BorderThickness' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (borderThickness!, "BorderThickness");
                }
                var shortName = ShortMemberDisplay(memberId);
                if (shortName is not null)
                {
                    if (targetType is null)
                        Fail<object>(offset, $"setter '{shortName}' in style entry {display} precedes its style TargetType (BAML emits TargetType first; style-surface follow-up)");
                    var resolved = DependencyProperty.FromName(targetType!, shortName);
                    if (resolved is null)
                        Fail<object>(offset, $"'{shortName}' is not a dependency property of {targetType!.Name} (setter in style entry {display})");
                    return (resolved!, shortName);
                }
                Fail<object>(offset, $"setter property {WellKnownMemberName(memberId)} in style entry {display} (short-form properties beyond {ShortMemberList()} are a style-surface follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            if (serializer == SerDependencyPropertyQualified)
            {
                var ownerId = ReadI16();
                var propertyName = ReadAnsiString();
                ExpectEnd(end, offset, "PropertyCustom");
                var owner = ResolvePropertyOwner(offset, display, ownerId);
                var dp = DependencyProperty.FromName(owner, propertyName);
                if (dp is null)
                    Fail<object>(offset, $"'{propertyName}' is not a dependency property of {owner.Name} (setter in style entry {display})");
                return (dp!, owner.Name + "." + propertyName);
            }
            Fail<object>(offset, $"setter in style entry {display} uses PropertyCustom serializer {serializer} (style-surface follow-up)");
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>Resolves a qualified PropertyCustom owner: negative ids
        /// are verified well-known framework types, non-negative ids are
        /// per-file TypeInfo rows resolving to engine types by full CLR
        /// name, else to the converted owner by full CLR name (OwnerType +
        /// PropertyName resolution against the staged converted lib: the
        /// staged BorderCircular Circular setter resolves
        /// HandyControl.Controls.BorderElement and lands its converted
        /// CircularProperty DP, which applies through the DP store like WPF
        /// attached setters; the staged Notification Window-qualified
        /// SizeToContent/ResizeMode/ShowActivated/ShowInTaskbar/Topmost
        /// setters resolve the engine Window the same well-known way; the
        /// staged RepeatButton OverridesDefaultStyle setter resolves the
        /// engine FrameworkElement the same way; the staged Gripper
        /// Canvas.Right setter resolves the engine Canvas, landing its
        /// attached RightProperty DP through the DP store like WPF attached
        /// setters; the staged ToolBarTrayBaseStyle Background setter resolves
        /// the engine ToolBarTray the same well-known way, landing the shared
        /// Panel Background identity, the MenuItem.Icon trigger condition resolves
        /// the engine MenuItem the same well-known way). Unloadable owners stay a precise follow-up.</summary>
        private Type ResolvePropertyOwner(int offset, string display, short ownerId)
        {
            if (ownerId == KnownShape) return typeof(Shape);
            if (ownerId == KnownRectangle) return typeof(Rectangle);
            if (ownerId == KnownUIElement) return typeof(UIElement);
            if (ownerId == KnownWindow) return typeof(Window);
            if (ownerId == KnownFrameworkElement) return typeof(FrameworkElement);
            if (ownerId == KnownCanvas) return typeof(Canvas);
            if (ownerId == KnownToolBarTray) return typeof(ToolBarTray);
            if (ownerId == KnownMenuItem) return typeof(MenuItem);
            if (ownerId < 0)
                Fail<object>(offset, $"setter property owner {WellKnownName(ownerId)} in style entry {display} (owners beyond Shape/UIElement/Window/FrameworkElement/Canvas/ToolBarTray/MenuItem are a style-surface follow-up)");
            if (ownerId >= _types.Count)
                Fail<object>(offset, $"setter property owner id {ownerId} in style entry {display} (only {_types.Count} TypeInfo records)");
            var fullName = TypeInfoName(ownerId);
            var shortName = fullName[(fullName.LastIndexOf('.') + 1)..];
            var engine = XamlLoader.FindEngineType(shortName);
            if (engine is not null) return engine;
            var converted = ResolveConvertedOwnerType(offset, display, fullName);
            if (converted is null)
            {
                Fail<object>(offset, $"setter property owner '{fullName}' in style entry {display} is a converted type (style-surface follow-up)");
                throw new InvalidOperationException("unreachable");
            }
            return converted;
        }

        /// <summary>Resolves a converted setter-property owner by full CLR
        /// name (the staged entry first, then a unique AppDomain scan
        /// excluding the engine assembly, mirroring entry-type resolution;
        /// ambiguous matches fail precise). Type-only: nothing is
        /// instantiated, so theme dictionaries resolve converted owners
        /// without running constructors.</summary>
        private Type? ResolveConvertedOwnerType(int offset, string display, string fullName)
        {
            var source = XamlLoader.CustomElementSource;
            if (source is not null && SafeGetType(source, fullName) is { } direct) return direct;
            Type? found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (ReferenceEquals(assembly, typeof(UIElement).Assembly)) continue;
                if (SafeGetType(assembly, fullName) is { } candidate && !ReferenceEquals(found, candidate))
                {
                    if (found is not null)
                    {
                        Fail<object>(offset, $"setter property owner '{fullName}' in style entry {display} matches converted types '{found.FullName}' and '{candidate.FullName}' (keep one type per full name)");
                        throw new InvalidOperationException("unreachable");
                    }
                    found = candidate;
                }
            }
            return found;
        }

        /// <summary>Requires a per-file attribute id to be the named member
        /// of the expected owner (file-independent: the table carries the
        /// meaning, never the index).</summary>
        private void RequireAttribute(int offset, string display, short attrId, short owner, string name)
        {
            if (attrId >= 0 && attrId < _attrs.Count)
            {
                var (ownerId, attrName) = _attrs[attrId];
                if (ownerId == owner && attrName == name) return;
                Fail<object>(offset, $"attribute '{attrName}' in style entry {display} (styles take {name}; style-surface follow-up)");
            }
            Fail<object>(offset, $"attribute id {attrId} in style entry {display} has no AttributeInfo record");
        }

        private static string WellKnownMemberName(short memberId) => memberId switch
        {
            KnownFlowDirection => "FlowDirection",
            KnownTemplateMember => "Template",
            KnownFocusableMember => "Focusable",
            KnownIsEnabledMember => "IsEnabled",
            KnownHAlignMember => "HorizontalAlignment",
            KnownVAlignMember => "VerticalAlignment",
            KnownHContentMember => "HorizontalContentAlignment",
            KnownVContentMember => "VerticalContentAlignment",
            KnownBackgroundMember => "Background",
            KnownHeightMember => "Height",
            KnownWidthMember => "Width",
            KnownFillMember => "Fill",
            KnownVisibilityMember => "Visibility",
            KnownBorderThicknessMember => "BorderThickness",
            KnownBorderBrushMember => "BorderBrush",
            KnownBorderBackgroundMember => "Background",
            KnownMarginAttribute => "Margin",
            KnownIsTabStopMember => "IsTabStop",
            KnownItemsPanelMember => "ItemsPanel",
                        KnownFiguresMember => "Figures",
            KnownDataMember => "Data",
            KnownRenderTransformMember => "RenderTransform",
            KnownImageSourceMember => "Source",
            KnownStyleMember => "Style",
            _ => $"well-known member id {memberId}",
        };

        private object ConvertScalar(int offset, string display, short typeId, short converter, string text)
        {
            if (typeId == KnownColor && converter == ConvColor) return ParseColorEntry(offset, display, text);
            if (typeId == KnownUInt32 && converter == ConvUInt32) return ParseUInt32Entry(offset, display, text);
            if (typeId == KnownDouble && converter == ConvDouble)
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return number;
                return Fail<object>(offset, $"'{text}' is not a Double for {display}");
            }
            if (typeId == KnownCornerRadius && converter == ConvCornerRadius)
            {
                try { return CornerRadius.Parse(text); }
                catch (FormatException ex)
                {
                    return Fail<object>(offset, $"'{text}' is not a CornerRadius for {display} ({ex.Message})");
                }
            }
            if (typeId == KnownThickness && converter == ConvThickness)
            {
                try { return Thickness.Parse(text); }
                catch (FormatException ex)
                {
                    return Fail<object>(offset, $"'{text}' is not a Thickness for {display} ({ex.Message})");
                }
            }
            if (typeId == KnownGeometry && converter == ConvGeometry)
            {
                try { return Geometry.Parse(text); }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    return Fail<object>(offset, $"'{text}' is not a Geometry for {display} ({ex.Message})");
                }
            }
            return Fail<object>(offset, $"unsupported BAML dictionary value for {display} (type id {typeId}, converter {converter}; v2 supports Color/ColorConverter, UInt32/UInt32Converter, Double/DoubleConverter, CornerRadius/CornerRadiusConverter, Thickness/ThicknessConverter and Geometry/GeometryConverter)");
        }

        /// <summary>Instantiates an empty-entry CLR type: engine resource
        /// types by short name (the BooleanToVisibilityConverter precedent),
        /// otherwise the converted type by full CLR name (the view-model
        /// precedent: single match with a default constructor). Anything else
        /// fails loud - never a fabricated value.</summary>
        private object InstantiateEntryType(int offset, string display, short typeId)
        {
            if (typeId == KnownBoolToVis) return new BooleanToVisibilityConverter();
            if (typeId == KnownMenuScrolling) return new MenuScrollingVisibilityConverter();
            var (type, engine) = ResolveEntryType(offset, display, typeId);
            try { return Activator.CreateInstance(type, nonPublic: true)!; }
            catch (Exception ex)
            {
                return Fail<object>(offset, engine
                    ? $"cannot instantiate engine type '{type.FullName}' for {display} ({ex.GetBaseException().Message})"
                    : $"cannot instantiate custom CLR type '{type.FullName}' for {display} ({ex.GetBaseException().Message})");
            }
        }

        /// <summary>Resolves an entry CLR type without instantiating it
        /// (converted-value property reflection validates against the type
        /// before any instance exists). The flag marks engine short-name
        /// matches for the instantiation error wording.</summary>
        private (Type Type, bool Engine) ResolveEntryType(int offset, string display, short typeId)
        {
            if (typeId < 0)
            {
                Fail<object>(offset, $"cannot instantiate {WellKnownName(typeId)} for {display} (empty {WellKnownName(typeId)} entries are not supported)");
                throw new InvalidOperationException("unreachable");
            }
            var fullName = TypeInfoName(typeId);
            var shortName = fullName[(fullName.LastIndexOf('.') + 1)..];
            var engine = XamlLoader.FindEngineType(shortName);
            if (engine is not null && !engine.IsAbstract && HasDefaultCtor(engine))
                return (engine, true);
            Type? found = null;
            var source = XamlLoader.CustomElementSource;
            if (source is not null && SafeGetType(source, fullName) is { } direct) found = direct;
            if (found is null)
            {
                var matches = new List<Type>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (ReferenceEquals(assembly, typeof(UIElement).Assembly)) continue;
                    if (SafeGetType(assembly, fullName) is { } candidate && !matches.Contains(candidate))
                        matches.Add(candidate);
                }
                if (matches.Count > 1)
                {
                    Fail<object>(offset, $"{display} matches converted types '{matches[0].FullName}' and '{matches[1].FullName}' (keep one type per full name)");
                    throw new InvalidOperationException("unreachable");
                }
                found = matches.Count == 1 ? matches[0] : null;
            }
            if (found is null)
            {
                Fail<object>(offset, $"cannot instantiate custom CLR type '{fullName}' for {display} (no loadable converted type with that full name)");
                throw new InvalidOperationException("unreachable");
            }
            if (found.IsAbstract)
            {
                Fail<object>(offset, $"cannot instantiate custom CLR type '{fullName}' for {display} (abstract type)");
                throw new InvalidOperationException("unreachable");
            }
            if (!HasDefaultCtor(found))
            {
                Fail<object>(offset, $"cannot instantiate custom CLR type '{fullName}' for {display} (needs a parameterless constructor)");
                throw new InvalidOperationException("unreachable");
            }
            return (found, false);
        }

        private static Type? SafeGetType(Assembly assembly, string fullName)
        {
            try { return assembly.GetType(fullName, throwOnError: false, ignoreCase: false); }
            catch { return null; }
        }

        private static bool HasDefaultCtor(Type type) =>
            type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null) is not null;

        private ResourceDictionary ActiveDictionary(int offset, string display) =>
            CurrentInlineDict ?? _dictRoot ?? Fail<ResourceDictionary>(offset, $"dictionary entry {display} outside any ResourceDictionary");

        private void AddEntry(int offset, object entryKey, string display, object? value)
        {
            try { ActiveDictionary(offset, display).Add(entryKey, value); }
            catch (InvalidOperationException ex) { throw new XamlLoadException($"BAML+0x{offset:X} (line {_line}, pos {_col}): duplicate dictionary key {display} ({ex.Message})"); }
            _keyIndex++;
        }

        private void AddDeferredEntry(int offset, object entryKey, string display, Func<object?> factory)
        {
            try { ActiveDictionary(offset, display).AddDeferred(entryKey, factory); }
            catch (InvalidOperationException ex) { throw new XamlLoadException($"BAML+0x{offset:X} (line {_line}, pos {_col}): duplicate dictionary key {display} ({ex.Message})"); }
            _keyIndex++;
        }

        private void EndElement(int offset)
        {
            if (_stack.Count == 0) Fail(offset, "unbalanced ElementEnd");
            if (_complexDepths.Count != 0 && _stack.Count == _complexDepths.Peek())
                Fail(offset, "ElementEnd closes a complex property (missing PropertyComplexEnd)");
            var (element, _) = _stack.Pop();
            if (element is DataTemplate template && template.BamlStartOffset >= 0
                && template.TemplateSource is null
                && _complexDepths.Count != 0 && _stack.Count == _complexDepths.Peek())
            {
                // Closing a complex-child DataTemplate: retain its exact
                // subtree bytes plus the table rows it can reference, so
                // LoadContent reparses fresh instances per call (independent
                // namescope and bindings, never a shared live root).
                template.SetRetainedSource(new BamlTemplateSource(
                    bytes[template.BamlStartOffset..(offset + 1)],
                    new(_assemblies),
                    new(_types),
                    new(_attrs),
                    new(_strings)));
            }
            if (_complexDepths.Count != 0 && _stack.Count == _complexDepths.Peek())
            {
                var (parent, member) = _stack.Peek();
                AttachComplexChild(offset, parent, member, element);
            }
            if (_stack.Count != 0 && (_complexDepths.Count == 0 || _stack.Count != _complexDepths.Peek()))
            {
                var peekElement = _stack.Peek().Element;
                if ((peekElement is TextBlock || peekElement is Span) && (element is TextBlock || element is Span))
                {
                    // Nested TextBlock / Span default content:
                    // Transfer the completed child into the parent as an inline via the
                    // reader attachment contract, preserving styling (FontWeight, FontStyle,
                    // FontSize, FontFamily, Foreground, Background, TextDecorations).
                    Span inlineToAttach;
                    if (element is Span existingSpan)
                    {
                        inlineToAttach = existingSpan;
                    }
                    else
                    {
                        var tbChild = (TextBlock)element;
                        var wrappedSpan = new Span
                        {
                            FontFamily = tbChild.FontFamily,
                            FontSize = tbChild.FontSize,
                            FontStyle = tbChild.FontStyle,
                            FontWeight = tbChild.FontWeight,
                            FontStretch = tbChild.FontStretch,
                            Foreground = tbChild.Foreground,
                            Background = tbChild.Background,
                            TextDecorations = tbChild.TextDecorations
                        };
                        if (tbChild.Inlines.Count > 0)
                        {
                            foreach (var childInline in tbChild.Inlines)
                            {
                                wrappedSpan.AddInline(childInline);
                            }
                        }
                        else
                        {
                            wrappedSpan.Text = tbChild.Text;
                        }
                        inlineToAttach = wrappedSpan;
                    }

                    if (peekElement is TextBlock tbParent)
                    {
                        tbParent.AddInline(inlineToAttach);
                    }
                    else if (peekElement is Span spParent)
                    {
                        spParent.AddInline(inlineToAttach);
                    }
                }
            }
            if (element is ResourceDictionary && _stack.Count == 0 && _expectDictionary)
            {
                // Closing the dictionary root: every index key must have an
                // entry (the entry machine consumes them in order).
                if (_keyIndex != _keys.Count)
                    Fail<object>(offset, $"dictionary closes with {_keys.Count - _keyIndex} unclaimed key(s) (first {DescribeKey(_keys[_keyIndex])})");
            }
        }

        /// <summary>Reads a sized 7-bit-prefixed string (validates the
        /// record end like every other sized record).</summary>
        private string ReadSizedString(int offset, string what)
        {
            var end = StartSized(out _);
            var value = ReadAnsiString();
            ExpectEnd(end, offset, what);
            return value;
        }

        private (string Text, short Converter) ReadTextWithConverterPayload(int offset)
        {
            var end = StartSized(out _);
            var value = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "TextWithConverter");
            return (value, conv);
        }

        /// <summary>Reads a PresentationOptions record (sized: 7-bit string
        /// value + U16 name id; Freeze="True" on freezables).</summary>
        private string ReadPresentationOptions(int offset)
        {
            var end = StartSized(out _);
            var value = ReadAnsiString();
            _pos += 2; // name id (Freeze attribute; value carries the meaning)
            ExpectEnd(end, offset, "PresentationOptionsAttribute");
            return value;
        }

        private string TypeInfoName(short typeId)
        {
            if (typeId < 0 || typeId >= _types.Count)
                return $"<unknown type id {typeId}>";
            return _types[typeId].Name;
        }

        private string TypeKeyName(short typeId)
        {
            if (typeId >= 0) return TypeInfoName(typeId);
            return WellKnownName(typeId);
        }

        private string DescribeKey(KeyRecord key) => key switch
        {
            StringKeyRecord s => $"'{s.Key}'",
            TypeKeyRecord t => $"(implicit style key '{TypeKeyName(t.TypeId)}')",
            StaticKeyRecord s => $"({{x:Static {DecodeStaticMember(s.Member ?? string.Empty)}}})",
            _ => "(unknown key)",
        };

        /// <summary>Decodes an element-form x:Static member name. Real key
        /// elements store it as a U8 name length, the "Type.Member" text and
        /// a '+' terminator (byte-verified on all 11 staged key elements:
        /// the prefix always equals the name length); anything else passes
        /// through for the precise error.</summary>
        private static string DecodeStaticMember(string raw)
        {
            if (raw.Length >= 2 && raw[^1] == '+' && raw[0] == (char)(raw.Length - 2))
                return raw[1..^1];
            return raw;
        }

        /// <summary>Well-known framework type names for precise entry errors
        /// (negated dotnet/wpf KnownElements indices, verified against the
        /// staged files).</summary>
        private static string WellKnownName(short typeId) => typeId switch
        {
            KnownColor => "Color",
            KnownUInt32 => "UInt32",
            KnownDouble => "Double",
            KnownCornerRadius => "CornerRadius",
            KnownGeometry => "Geometry",
            KnownPathGeometry => "PathGeometry",
            KnownBoolToVis => "BooleanToVisibilityConverter",
            KnownMenuScrolling => "MenuScrollingVisibilityConverter",
            KnownStyle => "Style",
            KnownSetter => "Setter",
            KnownTrigger => "Trigger",
            KnownDataTrigger => "DataTrigger",
            KnownControlTemplate => "ControlTemplate",
            KnownItemsPanelTemplate => "ItemsPanelTemplate",
            KnownDataTemplate => "DataTemplate",
            KnownImage => "Image",
            KnownLinearGradientBrush => "LinearGradientBrush",
            KnownSolidColorBrush => "SolidColorBrush",
            KnownGradientStop => "GradientStop",
            KnownDrawingBrush => "DrawingBrush",
            KnownDrawingGroup => "DrawingGroup",
            KnownGeometryDrawing => "GeometryDrawing",
            KnownGeometryGroup => "GeometryGroup",
            KnownRectangleGeometry => "RectangleGeometry",
            KnownUniformGrid => "UniformGrid",
            KnownBinding => "Binding",
            KnownTemplateBinding => "TemplateBindingExtension",
            KnownStaticExtension => "StaticExtension",
            KnownContextMenu => "ContextMenu",
            KnownWindow => "Window",
            KnownStoryboard => "Storyboard",
            KnownDoubleAnimation => "DoubleAnimation",
            KnownPath => "Path",
            KnownShape => "Shape",
            KnownUIElement => "UIElement",
            KnownThickness => "Thickness",
            KnownBorder => "Border",
            KnownContentControl => "ContentControl",
            KnownGridSplitter => "GridSplitter",
            KnownRectangle => "Rectangle",
            KnownRepeatButton => "RepeatButton",
            KnownGroupBox => "GroupBox",
            KnownThumb => "Thumb",
            KnownToolBarTray => "ToolBarTray",
            KnownComboBox => "ComboBox",
            KnownListBoxItem => "ListBoxItem",
            KnownCanvas => "Canvas",
            KnownViewport3D => "Viewport3D",
            KnownProjectionCamera => "ProjectionCamera",
            KnownPerspectiveCamera => "PerspectiveCamera",
            KnownModelVisual3D => "ModelVisual3D",
            KnownLight => "Light",
            KnownDirectionalLight => "DirectionalLight",
            KnownRowDefinition => "RowDefinition",
            KnownColumnDefinition => "ColumnDefinition",
            KnownTransformGroup => "TransformGroup",
            KnownRotateTransform => "RotateTransform",
            KnownStackPanel => "StackPanel",
            KnownVirtualizingStackPanel => "VirtualizingStackPanel",
            KnownWrapPanel => "WrapPanel",
            KnownGrid => "Grid",
            KnownRelativeSource => "RelativeSource",
            KnownContentPresenter => "ContentPresenter",
            KnownTextBlock => "TextBlock",
            KnownNullExtension => "NullExtension",
            _ => $"well-known type id {typeId}",
        };

        /// <summary>Record names for precise out-of-entry errors (ILSpy
        /// BamlRecordType names).</summary>
        private static string DescribeRecord(byte type) => type switch
        {
            0x05 => "Property",
            0x0B => "PropertyIListStart",
            0x0C => "PropertyIListEnd",
            0x06 => "PropertyCustom",
            0x07 => "PropertyComplexStart",
            0x08 => "PropertyComplexEnd",
            0x09 => "PropertyArrayStart",
            0x0A => "PropertyArrayEnd",
            0x0D => "PropertyDictionaryStart",
            0x0E => "PropertyDictionaryEnd",
            0x12 => "RoutedEvent",
            0x19 => "DefAttribute",
            0x21 => "PropertyStringReference",
            0x22 => "PropertyTypeReference",
            0x23 => "PropertyWithExtension",
            0x27 => "DefAttributeKeyType",
            0x28 => "KeyElementStart",
            0x29 => "KeyElementEnd",
            0x2F => "NamedElementStart",
            0x30 => "StaticResourceStart",
            0x31 => "StaticResourceEnd",
            0x32 => "StaticResourceId",
            0x33 => "TextWithId",
            0x34 => "PresentationOptionsAttribute",
            0x38 => "PropertyWithStaticResourceId",
            _ => $"record 0x{type:X2}",
        };

        private object ParseColorEntry(int offset, string display, string text)
        {
            try { return ColorConverter.ConvertFromString(text); }
            catch (FormatException ex)
            {
                return Fail<object>(offset, $"'{text}' is not a color for {display} ({ex.Message})");
            }
        }

        private object ParseUInt32Entry(int offset, string display, string text)
        {
            // The real UInt32Converter (hex-aware: the staged BlurGradientValue
            // reads "0x99FFFFFF"/"0x99000000"), driven through the framework
            // converter like real WPF instead of hand-rolled hex logic.
            try { return new UInt32Converter().ConvertFrom(null, CultureInfo.InvariantCulture, text)!; }
            catch (Exception ex) when (ex is FormatException or OverflowException or NotSupportedException or ArgumentException)
            {
                return Fail<object>(offset, $"'{text}' is not a UInt32 for {display} ({ex.Message})");
            }
        }

        private void ReadPropertyWithConverter(int offset)
        {
            var end = StartSized(out _);
            var attrId = ReadI16();
            var value = ReadAnsiString();
            var conv = ReadI16();
            ExpectEnd(end, offset, "PropertyWithConverter");
            if (_stack.Count == 0) Fail(offset, "property outside any element");
            ApplyProperty(offset, _stack.Peek().Element, attrId, value, conv);
        }

        private void ReadText(int offset)
        {
            var end = StartSized(out _);
            var value = ReadAnsiString();
            ExpectEnd(end, offset, "Text");
            if (_stack.Count == 0) Fail(offset, "text outside any element");
            if (IsDictionaryContent())
                Fail<object>(offset, "plain BAML text outside any entry (entries are element subtrees; style-surface follow-up)");
            var peek = _stack.Peek().Element;
            if (peek is TextBlock tb)
            {
                tb.AppendLiteral(value);
            }
            else if (peek is Span sp)
            {
                sp.AppendLiteral(value);
            }
            else
            {
                Fail<object>(offset, $"BAML text run inside {Describe(peek)} (v1 supports TextBlock/Span text only)");
            }
        }

        private object CreateElement(short typeId, int offset)
        {
            if (typeId >= 0)
            {
                // Per-file CLR type. Framework types without a well-known
                // short id (verified: DataGrid/DataGridTextColumn have none
                // and always travel by TypeInfo) are mapped by full CLR name
                // in the PresentationFramework assembly; the root custom
                // type is treated as a plain Window (codebehind ignored), except the
                // converted TestUserControl root below, and
                // other nested custom types are a precise boundary.
                if (_stack.Count == 0 && typeId < _types.Count)
                {
                    // Converted-component root contract (0x1F75 wall): a
                    // root per-file type naming the converted
                    // TestApp.TestUserControl element parses as a detached
                    // plain engine UserControl stand-in -- never the
                    // converted constructor (that ctor is itself running
                    // LoadComponent and would recurse). WpfAppBoot hydrates
                    // the stand-in onto the live component instance. The
                    // main-window root path (plain Window) is unchanged.
                    var (rootAsmId, rootTypeName) = _types[typeId];
                    var rootAsm = rootAsmId >= 0 && rootAsmId < _assemblies.Count ? _assemblies[rootAsmId] : string.Empty;
                    if (DockingIntegration.IsTestUserControlElement(rootTypeName, rootAsm))
                        return new UserControl();
                    if (rootTypeName is "System.Windows.Controls.UserControl" or "System.Windows.Controls.ContentControl")
                        return new UserControl();
                    return new Window();
                }
                if (typeId < _types.Count)
                {
                    var (asmId, typeName) = _types[typeId];
                    var asm = asmId >= 0 && asmId < _assemblies.Count ? _assemblies[asmId] : string.Empty;
                    if (asm.StartsWith("PresentationFramework", StringComparison.Ordinal))
                    {
                        if (typeName is "System.Windows.Controls.DataGrid") return new DataGrid();
                        if (typeName is "System.Windows.Controls.DataGridTextColumn") return new DataGridTextColumn();
                        // Items-cluster controls have no verified well-known
                        // short ids (never invent one); they travel by full CLR
                        // name like DataGrid does.
                        if (typeName is "System.Windows.Controls.ItemsControl") return new ItemsControl();
                        if (typeName is "System.Windows.Controls.HeaderedItemsControl") return new HeaderedItemsControl();
                        if (typeName is "System.Windows.Controls.ComboBox") return new ComboBox();
                        if (typeName is "System.Windows.Controls.ComboBoxItem") return new ComboBoxItem();
                        if (typeName is "System.Windows.Controls.ListView") return new ListView();
                        if (typeName is "System.Windows.Controls.ListViewItem") return new ListViewItem();
                        if (typeName is "System.Windows.Documents.Span") return new Span();
                        if (typeName is "System.Windows.Documents.Run") return new Run();
                        if (typeName is "System.Windows.Documents.Hyperlink") return new Hyperlink();
                        if (typeName is "System.Windows.Documents.Bold") return new Bold();
                        if (typeName is "System.Windows.Documents.Italic") return new Italic();
                        if (typeName is "System.Windows.Documents.Underline") return new Underline();
                        if (typeName is "System.Windows.Documents.LineBreak") return new LineBreak();
                        if (typeName is "System.Windows.Controls.UserControl") return new UserControl();
                    }
                    // Converted AvalonDock DockingManager: the declared
                    // joint-closure member resolves exactly and constructs as
                    // a real engine UIElement (DockingIntegration); anything
                    // else keeps the precise boundary below.
                    if (DockingIntegration.IsDockingManagerElement(typeName, asm))
                    {
                        var resolved = DockingIntegration.TryResolve(typeName);
                        if (resolved is null)
                        {
                            Fail<object>(offset, DockingIntegration.MissingClosureMessage(typeName));
                            throw new InvalidOperationException("unreachable");
                        }
                        if (!DockingIntegration.TryCreate(resolved, out var hosted, out var failure))
                        {
                            Fail<object>(offset, failure!);
                            throw new InvalidOperationException("unreachable");
                        }
                        return hosted!;
                    }
                    else if (DockingIntegration.IsHostedValueElement(typeName, asm))
                    {
                        // Converted values (theme objects, layout parts): exact
                        // closure member, concrete public contract type with a
                        // default constructor; the attach site decides placement.
                        if (!DockingIntegration.TryCreateValue(typeName, asm, out var valued, out var valueFailure))
                        {
                            Fail<object>(offset, valueFailure!);
                            throw new InvalidOperationException("unreachable");
                        }
                        return valued!;
                    }
                    else if (DockingIntegration.IsBridgeHostElement(typeName, asm))
                    {
                        // Real fork-backed bridge activation (HOST-CALL-1425):
                        // the exact joint-interface pair resolves through the
                        // engine-facing registry to the existing provider —
                        // never a scan, substitute or swallowed failure.
                        if (!WindowsFormsHostActivation.TryCreate(DockingIntegration.SimpleAssemblyName(asm), typeName, out var bridged, out var bridgeFailure))
                        {
                            Fail<object>(offset, bridgeFailure!);
                            throw new InvalidOperationException("unreachable");
                        }
                        return bridged!;
                    }
                    else if (DockingIntegration.IsTestUserControlElement(typeName, asm))
                    {
                        // Converted TestApp.TestUserControl (0x1F75 wall):
                        // the declared joint-closure member constructs as
                        // a real engine UserControl (DockingIntegration);
                        // anything else keeps the precise boundary below.
                        var resolved = DockingIntegration.TryResolveUserControl(typeName);
                        if (resolved is null)
                        {
                            Fail<object>(offset, DockingIntegration.MissingClosureMessage(typeName, DockingIntegration.TestUserControlAssemblyName));
                            throw new InvalidOperationException("unreachable");
                        }
                        if (!DockingIntegration.TryCreate(resolved, out var hosted, out var failure))
                        {
                            Fail<object>(offset, failure!);
                            throw new InvalidOperationException("unreachable");
                        }
                        return hosted!;
                    }
                }
                var name = typeId < _types.Count ? _types[typeId].Name : "?";
                Fail<object>(offset, $"cannot instantiate custom CLR type '{name}' in the browser engine (v1 supports Window/Grid/TextBlock/TextBox/ScrollViewer/ListBox/TreeView/Menu/DockPanel/DataGrid/DataGridTextColumn/ItemsControl/HeaderedItemsControl/ComboBox/ComboBoxItem/ListView/ListViewItem)");
                throw new InvalidOperationException("unreachable");
            }
            if (typeId == KnownBinding) return new Binding();
            if (typeId == KnownRelativeSource) return new RelativeSource();
            if (typeId == KnownRowDefinition) return new RowDefinition();
            if (typeId == KnownColumnDefinition) return new ColumnDefinition();
            return typeId switch
            {
                KnownWindow => new Window(),
                KnownGrid => new Grid(),
                KnownButton => new Button(),
                KnownTextBlock => new TextBlock(),
                KnownTextBox => new TextBox(),
                KnownScrollViewer => new ScrollViewer(),
                KnownListBox => new ListBox(),
                KnownListBoxItem => new ListBoxItem(),
                KnownTreeView => new TreeView(),
                KnownTreeViewItem => new TreeViewItem(),
                KnownMenu => new Menu(),
                KnownMenuItem => new MenuItem(),
                KnownSeparator => new Separator(),
                KnownDockPanel => new DockPanel(),
                KnownStackPanel => new StackPanel(),
                KnownDataTemplate => new DataTemplate(),
                KnownImage => new Image(),
                _ => Fail<UIElement>(offset, $"unknown well-known BAML type id {typeId} (v1 maps Window/Grid/TextBlock/TextBox/ScrollViewer/ListBox/TreeView/Menu/MenuItem/Separator/DockPanel/DataTemplate/Image/StackPanel only)"),
            };
        }

        private void AttachChild(int offset, object element)
        {
            var (parent, member) = _stack.Peek();
            if (_columnsOwner is DataGrid columnsOwner)
            {
                // Open <DataGrid.Columns> list (0x0B..0x0C): only column
                // elements attach here; anything nested deeper (e.g. a
                // Binding complex property) is a precise boundary.
                if (ReferenceEquals(parent, columnsOwner) && element is DataGridTextColumn column)
                { columnsOwner.AddColumn(column); return; }
                Fail<object>(offset, $"<Columns> list child {Describe(element)} on {Describe(parent)} (v1 supports DataGridTextColumn columns on a DataGrid only)");
            }
            if (_gridListOwner is Grid gridListOwner)
            {
                // Open <Grid.RowDefinitions>/<Grid.ColumnDefinitions> list:
                // only definition elements attach here.
                if (ReferenceEquals(parent, gridListOwner) && element is RowDefinition row && _gridListRows)
                { gridListOwner.RowDefinitions.Add(row); return; }
                if (ReferenceEquals(parent, gridListOwner) && element is ColumnDefinition col && !_gridListRows)
                { gridListOwner.ColumnDefinitions.Add(col); return; }
                Fail<object>(offset, $"<Grid> list child {Describe(element)} on {Describe(parent)} (v1 supports RowDefinition/ColumnDefinition rows only)");
            }
            if (_modelListOwner is object modelListOwner && ReferenceEquals(parent, modelListOwner))
            {
                // Open converted-model list: elements add in document order.
                if (_modelList is System.Collections.IList modelItems)
                {
                    try { modelItems.Add(element); return; }
                    catch (Exception ex) { Fail<object>(offset, $"model list rejected a child ({ex.GetBaseException().Message})"); }
                }
                Fail<object>(offset, $"model list child on a converted model (converted model lists take children only)");
            }
            if (member is short contentId && contentId >= 0 && contentId < _attrs.Count
                && DockingIntegration.IsHostedInstance(parent))
            {
                // Converted content value (verified: the real TestApp
                // mainwindow.baml sets the converted LayoutRoot model as
                // content member 26 "Layout" on the hosted DockingManager):
                // the converted DP type-checks the real converted instance;
                // anything else fails precise. Mirrors the complex-property
                // route in AttachComplexChild.
                var (_, contentName) = _attrs[contentId];
                if (DockingIntegration.TryApplyConvertedValue(parent, contentName, element, out var contentFailure))
                    return;
                Fail<object>(offset, contentFailure!);
                throw new InvalidOperationException("unreachable");
            }
            if (member is short modelContentId && modelContentId >= 0 && modelContentId < _attrs.Count
                && DockingIntegration.IsConvertedModelInstance(parent))
            {
                // Converted layout-model content child (verified: member 27
                // RootPanel on the converted LayoutRoot in the real TestApp
                // mainwindow.baml): the real CLR setter applies the parsed
                // converted instance; anything else fails precise. Convertible
                // hosts keep the converted-DP route above; engine parents
                // keep every branch below.
                var (_, modelContentName) = _attrs[modelContentId];
                if (DockingIntegration.TrySetConvertedModelMember(parent, modelContentName, element, out var modelContentFailure))
                    return;
                Fail<object>(offset, modelContentFailure!);
                throw new InvalidOperationException("unreachable");
            }
            if (member == KnownContent)
            {
                if (parent is Window w && element is UIElement wContent) { w.Content = wContent; return; }
                if (parent is ScrollViewer sv && element is UIElement svContent) { sv.Content = svContent; return; }
                // Converted-component root contract (0x69F wall): the real
                // testusercontrol.baml sets its child as Content on the
                // UserControl root; the parsed stand-in carries it so
                // WpfAppBoot can hydrate it onto the live component.
                if (parent is UserControl uc && element is UIElement ucContent) { uc.Content = ucContent; return; }
                Fail<object>(offset, $"Content member on {Describe(parent)} (v1 supports Window/ScrollViewer/UserControl Content only)");
            }
            else if (member == KnownChildren)
            {
                if (parent is Panel or Border) { AddPanelChild(offset, parent, element); return; }
                Fail<object>(offset, $"Children member on {Describe(parent)} (v1 supports Grid/StackPanel/Canvas/Border)");
            }
            else if (member == KnownVisualTree)
            {
                if (parent is DataTemplate dataTemplate)
                {
                    var visual = element as UIElement
                        ?? Fail<UIElement>(offset, $"VisualTree content member on <DataTemplate> holds {Describe(element)} (data templates take one visual)");
                    if (dataTemplate.VisualRoot is not null)
                        Fail<object>(offset, $"second visual on <DataTemplate> (data templates take one visual)");
                    dataTemplate.SetVisualRoot(visual);
                    return;
                }
                Fail<object>(offset, $"VisualTree content member on {Describe(parent)} (data templates take one visual; style-surface follow-up)");
            }
            else if (member == KnownItems)
            {
                if (parent is ListBox lb)
                {
                    if (element is ListBoxItem item) { lb.AddChild(item); return; }
                    Fail<object>(offset, $"Items member child {Describe(element)} on <ListBox> (v1 supports ListBoxItem rows only)");
                }
                if (parent is TreeView tv)
                {
                    if (element is TreeViewItem node) { tv.AddChild(node); return; }
                    Fail<object>(offset, $"Items member child {Describe(element)} on <TreeView> (v1 supports TreeViewItem nodes only)");
                }
                if (parent is TreeViewItem tvi)
                {
                    if (element is TreeViewItem node) { tvi.AddChild(node); return; }
                    Fail<object>(offset, $"Items member child {Describe(element)} on <TreeViewItem> (v1 supports TreeViewItem nodes only)");
                }
                if (parent is Menu menu)
                {
                    if (element is UIElement topRow && (topRow is MenuItem || topRow is Separator)) { menu.AddChild(topRow); return; }
                    Fail<object>(offset, $"Items member child {Describe(element)} on <Menu> (v1 supports MenuItem/Separator entries only)");
                }
                if (parent is MenuItem sub)
                {
                    if (element is UIElement subRow && (subRow is MenuItem || subRow is Separator)) { sub.AddChild(subRow); return; }
                    Fail<object>(offset, $"Items member child {Describe(element)} on <MenuItem> (v1 supports MenuItem/Separator entries only)");
                }
                if (parent is ItemsControl ic)
                {
                    ic.AddItem(element);
                    return;
                }
                Fail<object>(offset, $"Items member on {Describe(parent)} (v1 supports ListBox/TreeView/TreeViewItem/Menu/MenuItem/ItemsControl/ComboBox/ListView only)");
            }
            else if (member is null)
            {
                // Default content member by container type.
                if (parent is Window w && element is UIElement wContent) { w.Content = wContent; return; }
                if (parent is ScrollViewer sv && element is UIElement svContent) { sv.Content = svContent; return; }
                if (parent is UserControl uc && element is UIElement ucContent) { uc.Content = ucContent; return; }
                if (parent is ListBox lb && element is ListBoxItem item) { lb.AddChild(item); return; }
                if (parent is TreeView tv && element is TreeViewItem node) { tv.AddChild(node); return; }
                if (parent is TreeViewItem tvi && element is TreeViewItem child) { tvi.AddChild(child); return; }
                if (parent is Menu menu && element is UIElement dTop && (dTop is MenuItem || dTop is Separator)) { menu.AddChild(dTop); return; }
                if (parent is MenuItem sub && element is UIElement dSub && (dSub is MenuItem || dSub is Separator)) { sub.AddChild(dSub); return; }
                if (parent is Panel) { AddPanelChild(offset, parent, element); return; }
                if (parent is ItemsControl icc && element is UIElement iccContent) { icc.AddItem(iccContent); return; }
                if ((parent is TextBlock || parent is Span) && (element is TextBlock || element is Span))
                {
                    // Nested TextBlock / Span default content (verified: the genuine
                    // fw132 fixture nests a bold "0." TextBlock inside a
                    // wrapping TextBlock, and the real testusercontrol.baml
                    // carries it at 0x6D0): accepted here; child text and styling
                    // transfer lands at ElementEnd when the child run is complete
                    // via reader attachment contract (AddInline / AppendLiteral).
                    return;
                }
                if (parent is Border b && element is UIElement bContent)
                {
                    if (b.LogicalChildren.Count == 0) { b.AddChild(bContent); return; }
                    Fail<object>(offset, "Border accepts a single child");
                }
                Fail<object>(offset, $"default content inside {Describe(parent)} (v1 supports Window/ScrollViewer/UserControl/ListBoxItem-on-ListBox/TreeViewItem-on-TreeView/MenuItem/Separator-on-Menu/MenuItem/Grid/StackPanel/Canvas/DockPanel/Border/ItemsControl content)");
            }
            else
            {
                Fail<object>(offset, $"unknown BAML content member id {member} on {Describe(parent)}");
            }
            throw new InvalidOperationException("unreachable");
        }

        private static void AddPanelChild(int offset, object parent, object element)
        {
            if (parent is Border b && b.LogicalChildren.Count > 0)
                throw new XamlLoadException("Border accepts a single child");
            if (element is not UIElement ui)
                throw new XamlLoadException($"{Describe(element)} is not a visual child (DataGridTextColumn belongs in <DataGrid.Columns>)");
            if (parent is FrameworkElement fe) fe.AddChild(ui);
            else throw new XamlLoadException($"cannot add children to {parent.GetType().Name}");
        }

        private void ReadPropertyIListStart(int offset)
        {
            // PropertyIListStart is a bare record: 0x0B followed by the U16
            // AttributeInfo id of the list property (verified framing:
            // `0B <id-lo> <id-hi>`, no size prefix, unlike sized records).
            if (_pos + 2 > bytes.Length)
                Fail<object>(offset, "truncated BAML property-list record");
            var attrId = (int)ReadU16();
            if (_stack.Count == 0) Fail(offset, "property list outside any element");
            if (_columnsOwner is not null) Fail(offset, "nested property lists are not supported");
            if (_gridListOwner is not null) Fail(offset, "nested property lists are not supported");
            var parent = _stack.Peek().Element;
            if (attrId < _attrs.Count)
            {
                var (_, name) = _attrs[attrId];
                if (parent is DataGrid dg && name == "Columns") { _columnsOwner = dg; return; }
                if (parent is Grid gr && name == "RowDefinitions") { _gridListOwner = gr; _gridListRows = true; return; }
                if (parent is Grid gc && name == "ColumnDefinitions") { _gridListOwner = gc; _gridListRows = false; return; }
                if (DockingIntegration.IsConvertedModelInstance(parent))
                {
                    // Converted-model Children-style list: the named CLR property
                    // must be a live IList; elements Add in document order.
                    try
                    {
                        var listProperty = parent.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (listProperty is not null && listProperty.CanRead
                            && listProperty.GetValue(parent) is System.Collections.IList modelList)
                        {
                            _modelListOwner = parent;
                            _modelList = modelList;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Fail<object>(offset, $"model list is unavailable on a converted model ({ex.GetBaseException().Message})");
                    }
                }
            }
            var what = attrId < _attrs.Count ? $"'{_attrs[attrId].Name}'" : $"id {attrId}";
            Fail<object>(offset, $"unsupported property list {what} on {Describe(parent)} (v1 supports DataGrid.Columns and Grid RowDefinitions/ColumnDefinitions only)");
        }

        private void ReadPropertyIListEnd(int offset)
        {
            // PropertyIListEnd is a bare 0x0C byte with no payload.
            if (_columnsOwner is null && _gridListOwner is null && _modelListOwner is null) Fail(offset, "unbalanced BAML property-list end");
            _columnsOwner = null;
            _gridListOwner = null;
            _modelListOwner = null;
            _modelList = null;
        }

        private void ApplyProperty(int offset, object element, short attrId, string value, short conv)
        {
            if (element is ResourceDictionary dictionary)
            {
                if (_entriesStarted)
                    Fail<object>(offset, "BAML property on <ResourceDictionary> after entries started (corrupt dictionary content)");
                ApplyDictionaryProperty(offset, dictionary, attrId, value, conv);
                return;
            }
            if (element is UIElement rowFe && conv == ConvInt32 && attrId == KnownGridRowMember)
            {
                // Grid.Row attached property (verified: the main menu
                // carries Grid.Row 1 in the TestApp main window BAML).
                if (int.TryParse(value, out var row)) { Grid.SetRow(rowFe, row); return; }
                Fail<object>(offset, $"Grid.Row value is not an integer: {value}");
            }
            if (element is RowDefinition rowDef && (attrId == KnownRowHeightMember || (attrId >= 0 && attrId < _attrs.Count && _attrs[attrId].Name == "Height")))
            {
                try { rowDef.Height = GridLength.Parse(value); return; }
                catch (Exception ex) { Fail<object>(offset, $"'{value}' is not a grid length (RowDefinition.Height) ({ex.Message})"); }
            }
            if (element is ColumnDefinition colDef && (attrId >= 0 && attrId < _attrs.Count && _attrs[attrId].Name == "Width"))
            {
                try { colDef.Width = GridLength.Parse(value); return; }
                catch (Exception ex) { Fail<object>(offset, $"'{value}' is not a grid length (ColumnDefinition.Width) ({ex.Message})"); }
            }
            if (attrId >= 0)
            {
                if (DockingIntegration.IsConvertedModelInstance(element) && attrId < _attrs.Count)
                {
                    // Converted layout-model text value (LayoutPanel Orientation,
                    // DockWidth, titles and flags travel as per-file simples on model
                    // objects): converts through the shared set and applies via the
                    // real CLR setter; anything else fails precise.
                    var (_, modelAttrName) = _attrs[attrId];
                    if (DockingIntegration.TrySetConvertedModelText(element, modelAttrName, value, conv, out var modelTextFailure))
                        return;
                    Fail<object>(offset, modelTextFailure!);
                    throw new InvalidOperationException("unreachable");
                }
                // Per-file attribute (AttributeInfo table): Title/Icon/Name so far.
                if (attrId >= _attrs.Count)
                    Fail<object>(offset, $"BAML attribute id {attrId} has no AttributeInfo record");
                var (owner, name) = _attrs[attrId];
                if (element is MenuItem row)
                {
                    // In-content menu rows (verified: the TestApp main window
                    // carries converted Command values plus inline Header,
                    // Tag, IsCheckable and IsChecked values on MenuItem
                    // elements; Header itself rides the well-known member).
                    if (owner == KnownMenuItem && name == "Command")
                    {
                        row.Command = ReadMenuItemCommand(offset, $"BAML+0x{offset:X}", attrId, value, conv, "menu row");
                        return;
                    }
                    if (owner == KnownMenuItem && name == "IsCheckable" && conv == ConvBool)
                    {
                        if (bool.TryParse(value, out var checkable)) { row.IsCheckable = checkable; return; }
                        Fail<object>(offset, $"'{value}' is not a boolean for IsCheckable");
                    }
                    if (owner == KnownMenuItem && name == "IsChecked" && conv == ConvBool)
                    {
                        if (bool.TryParse(value, out var @checked)) { row.IsChecked = @checked; return; }
                        Fail<object>(offset, $"'{value}' is not a boolean for IsChecked");
                    }
                    if (name == "Name" && conv == ConvString) { row.Name = value; RegisterDocumentName(row, value, offset); return; }
                    if (owner == KnownFrameworkElement && name == "Tag" && conv == ConvString)
                    { row.Tag = value; return; }
                    Fail<object>(offset, $"BAML attribute '{name}' on <MenuItem> (menu rows take Command/Header/Name/Tag/IsCheckable/IsChecked; menu-surface follow-up)");
                    throw new InvalidOperationException("unreachable");
                }
                if (name == "Name" && conv == ConvString && element is FrameworkElement namedFe)
                { namedFe.Name = value; RegisterDocumentName(element, value, offset); return; }
                if (DockingIntegration.IsHostedInstance(element))
                {
                    // Converted-DP apply for the real hosted instance:
                    // the converted type's own DependencyProperty converts
                    // the literal through the engine converter; unknown DPs,
                    // mismatched converters and bad values fail precise.
                    if (!DockingIntegration.TryApplyConvertedProperty(element, name, value, conv, out var hostedFailure))
                    {
                        Fail<object>(offset, hostedFailure!);
                        throw new InvalidOperationException("unreachable");
                    }
                    return;
                }
                if (element is StackPanel stackPanel && owner == KnownStackPanel && name == "Orientation" && conv == ConvEnum)
                {
                    // StackPanel.Orientation travels as a per-file enum
                    // simple (verified: the #104 FlipClock template visual
                    // plus the real TestApp DocumentHeaderTemplate visual at
                    // BAML+0x13DB, both "Horizontal"/EnumConverter).
                    if (value == "Horizontal") { stackPanel.Orientation = Orientation.Horizontal; return; }
                    if (value == "Vertical") { stackPanel.Orientation = Orientation.Vertical; return; }
                    Fail<object>(offset, $"'{value}' is not an Orientation for StackPanel.Orientation");
                }
                if (element is Binding modeBinding && name == "Mode" && conv == ConvEnum)
                {
                    // Binding.Mode travels as a per-file EnumConverter
                    // simple (verified: the genuine fw127 fixture compiling
                    // Mode=OneWay emits it exactly so, and the real TestApp
                    // file carries it at BAML+0x1CFF).
                    try { modeBinding.Mode = Enum.Parse<BindingMode>(value, ignoreCase: false); }
                    catch (ArgumentException)
                    {
                        Fail<object>(offset, $"'{value}' is not a BindingMode for Mode (binding modes take Default/TwoWay/OneWay/OneTime/OneWayToSource; follow-up)");
                    }
                    return;
                }
                if (element is Binding fmtBinding && name == "StringFormat" && conv == ConvString)
                {
                    // Binding.StringFormat travels as a per-file
                    // StringConverter simple (verified: the genuine fw128
                    // fixture compiling StringFormat='Document: {0}' emits
                    // it exactly so, and the real TestApp file carries it
                    // at BAML+0x1D20): lands verbatim, braces included.
                    fmtBinding.StringFormat = value;
                    return;
                }
                if (element is RelativeSource rss && name == "AncestorLevel" && conv == ConvInt32)
                {
                    // RelativeSource.AncestorLevel travels as a per-file
                    // int32 simple (verified: the genuine fw126 fixture
                    // compiling FindAncestor/Window/1 emits it exactly so;
                    // sits with the pre-guard per-file arms since the guard
                    // below allowlists only visual hosts).
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var level))
                    { rss.AncestorLevel = level; return; }
                    Fail<object>(offset, $"'{value}' is not an integer for AncestorLevel");
                }
                if (element is UIElement visibilityElement && name == "Visibility" && conv == ConvEnum)
                {
                    // UIElement.Visibility travels as a per-file enum
                    // simple (real TestApp DocumentHeaderTemplate Title text
                    // carries "Collapsed"/EnumConverter).
                    if (value == "Visible") { visibilityElement.Visibility = Visibility.Visible; return; }
                    if (value == "Hidden") { visibilityElement.Visibility = Visibility.Hidden; return; }
                    if (value == "Collapsed") { visibilityElement.Visibility = Visibility.Collapsed; return; }
                    Fail<object>(offset, $"'{value}' is not a Visibility on {Describe(element)}");
                }
                if (element is not Window && element is not ListBox && element is not TreeViewItem
                    && element is not DataGrid && element is not DataGridTextColumn
                    && element is not ItemsControl && element is not Binding)
                    Fail<object>(offset, $"BAML attribute '{name}' on {Describe(element)} (v1 supports Window.Title/Icon, ListBox/DataGrid/Selector.SelectedIndex, TreeViewItem.IsExpanded, DataGridTextColumn.Header/Width, ComboBox.Text/IsDropDownOpen/IsEditable, ComboBoxItem.Content, ItemsControl.DisplayMemberPath and Binding.ElementName/Path only)");
                if (name == "Title" && conv == ConvString) { ((Window)element).Title = value; return; }
                if (name == "Icon" && conv == ConvImage)
                {
                    var window = (Window)element;
                    window.IconSource = value;
                    window.Icon = new BitmapImage(new Uri(value, UriKind.RelativeOrAbsolute));
                    return;
                }
                // Selector.SelectedIndex takes any int32 converter: the value
                // domain is self-validating (a non-integer fails precisely
                // below), so the converter id is not pinned like Title/Icon.
                if (element is ListBox lb && name == "SelectedIndex")
                {
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var index))
                    { lb.SelectedIndex = index; return; }
                    Fail<object>(offset, $"'{value}' is not an integer for SelectedIndex");
                }
                // TreeViewItem.IsExpanded takes any bool converter: the value
                // domain is self-validating (bool.TryParse mirrors the XAML
                // ParseBool), so the converter id is not pinned.
                if (element is TreeViewItem tvi && name == "IsExpanded")
                {
                    if (bool.TryParse(value, out var expanded)) { tvi.IsExpanded = expanded; return; }
                    Fail<object>(offset, $"'{value}' is not a boolean for IsExpanded");
                }
                // Selector.SelectedIndex on DataGrid mirrors the ListBox path
                // (per-file attribute taking any int32 converter).
                if (element is DataGrid dg && name == "SelectedIndex")
                {
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var index))
                    { dg.SelectedIndex = index; return; }
                    Fail<object>(offset, $"'{value}' is not an integer for SelectedIndex");
                }
                // Selector.SelectedIndex on the new hierarchy (ComboBox and
                // future Selector XAML; value domain self-validates like ListBox).
                if (element is Selector sel && name == "SelectedIndex")
                {
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var index))
                    { sel.SelectedIndex = index; return; }
                    Fail<object>(offset, $"'{value}' is not an integer for SelectedIndex");
                }
                // ComboBox text/drop knobs (string and bool converters).
                if (element is ComboBox cb && name == "Text" && conv == ConvString)
                { cb.Text = value; return; }
                if (element is ComboBox cbb && name == "IsDropDownOpen" && conv == ConvBool)
                {
                    if (bool.TryParse(value, out var open)) { cbb.IsDropDownOpen = open; return; }
                    Fail<object>(offset, $"'{value}' is not a boolean for IsDropDownOpen");
                }
                if (element is ComboBox cbe && name == "IsEditable" && conv == ConvBool)
                {
                    if (bool.TryParse(value, out var editable)) { cbe.IsEditable = editable; return; }
                    Fail<object>(offset, $"'{value}' is not a boolean for IsEditable");
                }
                if (element is ComboBoxItem cbi && name == "Content" && conv == ConvString)
                { cbi.Content = value; return; }
                if (element is ComboBoxItem cbis && name == "IsSelected" && conv == ConvBool)
                {
                    if (bool.TryParse(value, out var selected)) { cbis.IsSelected = selected; return; }
                    Fail<object>(offset, $"'{value}' is not a boolean for IsSelected");
                }
                if (element is ItemsControl icp && name == "DisplayMemberPath" && conv == ConvString)
                { icp.DisplayMemberPath = value; return; }
                if (element is Binding bb && name == "ElementName" && conv == ConvString)
                { bb.ElementName = value; return; }
                if (element is Binding bbp && name == "Path" && conv == ConvString)
                { bbp.Path = new PropertyPath(value); return; }
                // UIElement.UseLayoutRounding takes the bool converter (verified:
                // the TestApp main window carries it as a per-file attribute).
                if (element is UIElement uie && name == "UseLayoutRounding" && conv == ConvBool)
                {
                    if (bool.TryParse(value, out var round)) { uie.UseLayoutRounding = round; return; }
                    Fail<object>(offset, $"'{value}' is not a boolean for UseLayoutRounding");
                }
                // DataGridTextColumn.Header takes the StringConverter.
                if (element is DataGridTextColumn col && name == "Header" && conv == ConvString)
                { col.Header = value; return; }
                // DataGridColumn.Width takes its converter by TypeInfo (not a
                // well-known id): accept any converter, the grid-length value
                // domain is self-validating via GridLength.Parse.
                if (element is DataGridTextColumn colW && name == "Width")
                {
                    try { colW.Width = GridLength.Parse(value); return; }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
                    { Fail<object>(offset, $"'{value}' is not a grid length for Width"); }
                }
                Fail<object>(offset, $"unsupported BAML attribute '{name}' with converter {conv} (v1 supports Title/StringConverter, Icon/ImageSourceConverter, SelectedIndex/int32, IsExpanded/bool, Header/StringConverter, Width/DataGridLengthConverter, ComboBox.Text/StringConverter, ComboBox.IsDropDownOpen+IsEditable/bool, ComboBoxItem.Content+IsSelected, ItemsControl.DisplayMemberPath/StringConverter, UseLayoutRounding/bool, StackPanel.Orientation/EnumConverter and Visibility/EnumConverter)");
                throw new InvalidOperationException("unreachable");
            }
            switch (attrId)
            {
                case KnownHeight when conv == ConvLength:
                case KnownWidth when conv == ConvLength:
                    var fe = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"size member on {Describe(element)}");
                    try
                    {
                        var d = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        if (attrId == KnownHeight) fe.Height = d; else fe.Width = d;
                    }
                    catch (FormatException)
                    {
                        Fail<object>(offset, $"'{value}' is not a number for Height/Width");
                    }
                    break;
                case KnownMinHeightMember when conv == ConvLength:
                    var feMin = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"MinHeight member on {Describe(element)}");
                    try { feMin.MinHeight = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); }
                    catch (FormatException)
                    {
                        Fail<object>(offset, $"'{value}' is not a number for MinHeight");
                    }
                    break;
                case KnownMinWidthMember when conv == ConvLength:
                    var feMinW = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"MinWidth member on {Describe(element)}");
                    try { feMinW.MinWidth = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); }
                    catch (FormatException)
                    {
                        Fail<object>(offset, $"'{value}' is not a number for MinWidth");
                    }
                    break;
                case KnownTextWrappingMember when conv == ConvEnum:
                    var wrapBlock = element as TextBlock
                        ?? Fail<TextBlock>(offset, $"BAML member -113 (TextBlock.TextWrapping) on {Describe(element)}");
                    try { wrapBlock.TextWrapping = Enum.Parse<TextWrapping>(value, ignoreCase: false); }
                    catch (ArgumentException)
                    {
                        Fail<object>(offset, $"'{value}' is not a TextWrapping for TextWrapping (text wrapping takes WrapWithOverflow/NoWrap/Wrap; follow-up)");
                    }
                    break;
                case KnownFontWeightMember when conv == ConvFontWeight:
                    var fwBlock = element as TextBlock
                        ?? Fail<TextBlock>(offset, $"BAML member -108 (TextBlock.FontWeight) on {Describe(element)}");
                    try { fwBlock.FontWeight = (FontWeight)new FontWeightConverter().ConvertFrom(null, System.Globalization.CultureInfo.InvariantCulture, value)!; }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
                    {
                        Fail<object>(offset, $"'{value}' is not a FontWeight for FontWeight ({ex.GetBaseException().Message}; follow-up)");
                    }
                    break;
                case KnownHAlign when conv == ConvEnum:
                    var feH = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"HorizontalAlignment on {Describe(element)}");
                    feH.HorizontalAlignment = ParseHAlign(offset, value);
                    break;
                case KnownVAlign when conv == ConvEnum:
                    var feV = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"VerticalAlignment on {Describe(element)}");
                    feV.VerticalAlignment = ParseVAlign(offset, value);
                    break;
                case KnownTextBoxText when conv == ConvString:
                    var tbb = element as TextBox
                        ?? Fail<TextBox>(offset, $"BAML member -114 (TextBox.Text) on {Describe(element)}");
                    tbb.Text = value;
                    break;
                case KnownTextBlockText when conv == ConvString:
                    var tbl = element as TextBlock
                        ?? Fail<TextBlock>(offset, $"BAML member -110 (TextBlock.Text) on {Describe(element)}");
                    tbl.Text = value;
                    break;
                case KnownContent when conv == ConvString:
                    if (element is ListBoxItem lbi) { lbi.Content = value; break; }
                    if (element is ComboBoxItem cbiContent) { cbiContent.Content = value; break; }
                    if (element is Button btn) { btn.Content = value; break; }
                    Fail<object>(offset, $"BAML member -14 (ContentControl.Content) on {Describe(element)}");
                    break;
                case KnownHeader when conv == ConvString:
                    // HeaderedItemsControl.Header is shared by TreeViewItem
                    // and MenuItem (verified: the Menu BAML fixture emits
                    // member -71 for MenuItem Header exactly like TreeView).
                    if (element is TreeViewItem tvi) { tvi.Header = value; break; }
                    if (element is MenuItem mi) { mi.Header = value; break; }
                    if (element is HeaderedItemsControl hic) { hic.Header = value; break; }
                    Fail<object>(offset, $"BAML member -71 (HeaderedItemsControl.Header) on {Describe(element)}");
                    break;
                case KnownIsEnabled when conv == ConvBool:
                    var uie = element as UIElement
                        ?? Fail<UIElement>(offset, $"IsEnabled member on {Describe(element)}");
                    if (bool.TryParse(value, out var enabled)) { uie.IsEnabled = enabled; break; }
                    Fail<object>(offset, $"'{value}' is not a boolean for IsEnabled");
                    break;
                case KnownDock when conv == ConvEnum:
                    // DockPanel.Dock travels as an enum string ("Top", ...);
                    // stored as the attached value so DockPanel.GetDock (and
                    // the loose-XAML path) reads one representation.
                    var docked = element as UIElement
                        ?? Fail<UIElement>(offset, $"Dock member on {Describe(element)}");
                    if (DockPanel.TryParseDock(value, out var dock)) { DockPanel.SetDock(docked, dock); break; }
                    Fail<object>(offset, $"'{value}' is not a Dock (Left/Top/Right/Bottom)");
                    break;
                case KnownTextTrimmingMember when conv == ConvEnum:
                    var trimBlock = element as TextBlock
                        ?? Fail<TextBlock>(offset, $"TextTrimming member on {Describe(element)}");
                    if (value == "None") { trimBlock.TextTrimming = TextTrimming.None; break; }
                    if (value == "CharacterEllipsis") { trimBlock.TextTrimming = TextTrimming.CharacterEllipsis; break; }
                    if (value == "WordEllipsis") { trimBlock.TextTrimming = TextTrimming.WordEllipsis; break; }
                    Fail<object>(offset, $"'{value}' is not a TextTrimming");
                    break;
                case KnownMarginAttribute when conv == ConvThickness:
                    var marginFe = element as FrameworkElement
                        ?? Fail<FrameworkElement>(offset, $"Margin member on {Describe(element)}");
                    try { marginFe.Margin = Thickness.Parse(value); }
                    catch (FormatException)
                    {
                        Fail<object>(offset, $"'{value}' is not a Thickness for Margin");
                    }
                    break;
                case KnownTextBoxBorderThicknessMember when conv == ConvThickness:
                    var btBox = element as TextBox
                        ?? Fail<TextBox>(offset, $"BAML member -26 (TextBox.BorderThickness) on {Describe(element)}");
                    try { btBox.BorderThickness = Thickness.Parse(value); }
                    catch (FormatException)
                    {
                        Fail<object>(offset, $"'{value}' is not a Thickness for BorderThickness");
                    }
                    break;
                case KnownLastChildFill when conv == ConvBool:
                    var dp = element as DockPanel
                        ?? Fail<DockPanel>(offset, $"BAML member -40 (DockPanel.LastChildFill) on {Describe(element)}");
                    if (bool.TryParse(value, out var fill)) { dp.LastChildFill = fill; break; }
                    Fail<object>(offset, $"'{value}' is not a boolean for LastChildFill");
                    break;
                case KnownName when conv == ConvString:
                    if (element is FrameworkElement named) { named.Name = value; RegisterDocumentName(element, value, offset); }
                    else
                        Fail<object>(offset, $"Name on {Describe(element)} (FrameworkElement only)");
                    break;
                default:
                    Fail<object>(offset, $"unsupported BAML member id {attrId} with converter {conv} on {Describe(element)}");
                    break;
            }
        }

        private void ApplyDictionaryProperty(int offset, ResourceDictionary dictionary, short attrId, string value, short conv)
        {
            // The merge-root form (<ResourceDictionary Source="pack://..."/>):
            // the Source string is captured for the caller to resolve (see
            // XamlLoader.LoadBamlDictionary); inline content beside Source=
            // is a precise boundary like the loose reader.
            if (attrId >= 0)
            {
                if (attrId >= _attrs.Count)
                    Fail<object>(offset, $"BAML attribute id {attrId} has no AttributeInfo record");
                var (_, name) = _attrs[attrId];
                if (name == "Source" && conv == ConvUri)
                {
                    try { dictionary.Source = new Uri(value, UriKind.RelativeOrAbsolute); }
                    catch (UriFormatException ex)
                    {
                        Fail<object>(offset, $"'{value}' is not a valid dictionary Source ({ex.Message})");
                    }
                    return;
                }
                Fail<object>(offset, $"unsupported BAML attribute '{name}' with converter {conv} on <ResourceDictionary> (v1 supports Source/UriConverter only)");
                throw new InvalidOperationException("unreachable");
            }
            Fail<object>(offset, $"unsupported BAML member id {attrId} with converter {conv} on <ResourceDictionary> (v1 supports Source/UriConverter only)");
        }

        private static HorizontalAlignment ParseHAlign(int offset, string value) => value switch
        {
            "Left" => HorizontalAlignment.Left,
            "Center" => HorizontalAlignment.Center,
            "Right" => HorizontalAlignment.Right,
            "Stretch" => HorizontalAlignment.Stretch,
            _ => throw new XamlLoadException($"BAML+0x{offset:X}: '{value}' is not a HorizontalAlignment"),
        };

        private static VerticalAlignment ParseVAlign(int offset, string value) => value switch
        {
            "Top" => VerticalAlignment.Top,
            "Center" => VerticalAlignment.Center,
            "Bottom" => VerticalAlignment.Bottom,
            _ => throw new XamlLoadException($"BAML+0x{offset:X}: '{value}' is not a VerticalAlignment"),
        };

        private static string Describe(object e) => $"<{e.GetType().Name}>";

        private int StartSized(out int offset)
        {
            offset = _pos - 1; // type byte already consumed
            var sizeStart = _pos;
            var size = Read7Bit();
            return sizeStart + size; // size includes its own byte(s)
        }

        private void ExpectEnd(int end, int offset, string what)
        {
            if (_pos != end)
                Fail<object>(offset, $"misparsed BAML {what} record (at 0x{_pos:X}, expected end 0x{end:X})");
        }

        private byte ReadByte() => bytes[_pos++];
        private ushort ReadU16() { var v = (ushort)(bytes[_pos] | (bytes[_pos + 1] << 8)); _pos += 2; return v; }
        private short ReadI16() => (short)ReadU16();
        private uint ReadU32() { var v = (uint)(bytes[_pos] | (bytes[_pos + 1] << 8) | (bytes[_pos + 2] << 16) | (bytes[_pos + 3] << 24)); _pos += 4; return v; }
        private int ReadI32() => (int)ReadU32();

        private int Read7Bit()
        {
            var v = 0; var s = 0;
            while (true) { var x = bytes[_pos++]; v |= (x & 0x7F) << s; if ((x & 0x80) == 0) break; s += 7; }
            return v;
        }

        private string ReadAnsiString()
        {
            var n = Read7Bit();
            var s = Encoding.ASCII.GetString(bytes, _pos, n);
            _pos += n;
            return s;
        }

        private T Fail<T>(int offset, string message) =>
            throw new XamlLoadException($"BAML+0x{offset:X} (line {_line}, pos {_col}): {message}");

        /// <summary>Non-generic overload for statement-position failures.</summary>
        private UIElement Fail(int offset, string message) => Fail<UIElement>(offset, message);
    }
}


