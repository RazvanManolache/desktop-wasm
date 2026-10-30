using System.Reflection;

namespace DesktopWasm.Wpf;

// Template cluster (PresentationFramework subset, shim, not port): real
// template ownership and inflation for the ScreenToGif control pattern
// (Template.FindName parts, TemplateBinding, ControlTemplate triggers) and
// the Gallery boot-chain templates (PageHeader, BorderlessButtonStyle).
// Converted ScreenToGif controls consume templates through Control.Template
// (Template.FindName("StatusControlGrid", this),
// Template.FindName("PathRoot", this), GetTemplateChild(...)); converted
// gallery controls consume them through implicit styles with BasedOn and
// {TemplateBinding}/{Binding RelativeSource=TemplatedParent} parts.
//
// Namespace rule: every public type here keeps the WPF short name so the
// converter's name map binds it without an override.

/// <summary>Template base (matches System.Windows.FrameworkTemplate): shared
/// resources plus per-call visual factories; DataTemplate and ControlTemplate
/// derive here, like real WPF.</summary>
public abstract class FrameworkTemplate
{
    private FrameworkElementFactory? _visualTree;

    /// <summary>Template-local resources (retained; lookup at inflation).</summary>
    public ResourceDictionary Resources { get; } = new();

    /// <summary>
    /// Factory-built visual tree (matches FrameworkTemplate.VisualTree):
    /// converted code-built templates install their root factory here
    /// (converted AvalonDock calls the FrameworkTemplate-declared setter).
    /// Assigning seals the factory, like template use in WPF, and notifies
    /// the template through OnFactoryTreeSet so ControlTemplate can inflate
    /// its retained root from it.
    /// </summary>
    public virtual FrameworkElementFactory? VisualTree
    {
        get => _visualTree;
        set
        {
            value?.Seal();
            _visualTree = value;
            OnFactoryTreeSet(value);
        }
    }

    /// <summary>Factory-tree hook (ControlTemplate inflates its retained root).</summary>
    protected virtual void OnFactoryTreeSet(FrameworkElementFactory? factory) { }

    /// <summary>Inflates one fresh visual from the retained template.</summary>
    public abstract object LoadContent();

    /// <summary>Finds a named part in the tree inflated for a templated
    /// parent (matches FrameworkTemplate.FindName; ControlTemplate overrides
    /// it with its per-parent namescope, data templates keep no namescope
    /// and stay null).</summary>
    public virtual object? FindName(string name, FrameworkElement templatedParent) => null;
}

/// <summary>Template data record (matches System.Windows.DataTemplateSelector):
/// per-item visual choice for ItemsControl.ItemTemplateSelector; the
/// ScreenToGif ComboBoxItemTemplateSelector derives from this contract with
/// SelectedTemplate/DropDownTemplate attached properties.</summary>
/// <summary>
/// Factory-built template visual (matches System.Windows.FrameworkElementFactory):
/// code-built template trees like converted AvalonDock's show/hide button
/// template (Border root with a Foreground-bound Path child, values set plus
/// a RelativeSource binding, assembled with AppendChild and installed through
/// FrameworkTemplate.VisualTree). Parents chain through FirstChild/NextSibling
/// like WPF; SetValue/SetBinding/SetResourceReference/AddHandler record the
/// per-instance writes; CreateInstance builds a fresh tree per call
/// (parameterless construction, Text landing on a string Text property,
/// children parented through Panel/Content/Decorator hosts, resource
/// references resolved against application scope). Sealed factories reject
/// mutation (assigning VisualTree seals, like template use in WPF).
/// </summary>
public class FrameworkElementFactory
{
    private readonly List<(DependencyProperty Property, object? Value)> _values = new();
    private readonly List<(DependencyProperty Property, BindingBase Binding)> _bindings = new();
    private readonly List<(DependencyProperty Property, object Key)> _resources = new();
    private readonly List<(RoutedEvent Event, Delegate Handler, bool HandledToo)> _handlers = new();
    private readonly List<FrameworkElementFactory> _children = new();
    private bool _sealed;
    private string? _name;
    private string? _text;
    private Type? _type;

    public FrameworkElementFactory()
    {
    }

    public FrameworkElementFactory(string text) => _text = text;

    public FrameworkElementFactory(Type type) => Type = type;

    public FrameworkElementFactory(Type type, string name)
    {
        Type = type;
        Name = name;
    }

    /// <summary>Instantiated element type (null until set).</summary>
    public Type? Type
    {
        get => _type;
        set
        {
            ThrowIfSealed();
            _type = value;
        }
    }

    /// <summary>Template namescope name (matches Name).</summary>
    public string? Name
    {
        get => _name;
        set
        {
            ThrowIfSealed();
            _name = value;
        }
    }

    /// <summary>Text content (matches Text; lands on a string Text property at creation).</summary>
    public string? Text
    {
        get => _text;
        set
        {
            ThrowIfSealed();
            _text = value;
        }
    }

    /// <summary>First child factory (null when childless).</summary>
    public FrameworkElementFactory? FirstChild => _children.Count == 0 ? null : _children[0];

    /// <summary>Next sibling factory (null at the end or when unparented).</summary>
    public FrameworkElementFactory? NextSibling
    {
        get
        {
            if (Parent is null) return null;
            var siblings = Parent._children;
            var index = siblings.IndexOf(this);
            return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
        }
    }

    /// <summary>Parent factory (null at the root).</summary>
    public FrameworkElementFactory? Parent { get; private set; }

    /// <summary>Sealed factories reject mutation (matches IsSealed).</summary>
    public bool IsSealed => _sealed;

    /// <summary>Seals this factory and its subtree (template assignment seals, like WPF).</summary>
    internal void Seal()
    {
        _sealed = true;
        foreach (var child in _children) child.Seal();
    }

    public void SetValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfSealed();
        _values.Add((dp, value));
    }

    public void SetBinding(DependencyProperty dp, BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(binding);
        ThrowIfSealed();
        _bindings.Add((dp, binding));
    }

    public void SetResourceReference(DependencyProperty dp, object resourceKey)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(resourceKey);
        ThrowIfSealed();
        _resources.Add((dp, resourceKey));
    }

    public void AddHandler(RoutedEvent routedEvent, Delegate handler) =>
        AddHandler(routedEvent, handler, false);

    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfSealed();
        _handlers.Add((routedEvent, handler, handledEventsToo));
    }

    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfSealed();
        _handlers.RemoveAll(h => ReferenceEquals(h.Event, routedEvent) && Equals(h.Handler, handler));
    }

    public void AppendChild(FrameworkElementFactory child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ThrowIfSealed();
        if (child.Parent is not null)
            throw new InvalidOperationException("The factory is already a child of another factory.");
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>
    /// Builds one fresh instance tree: news up Type (parameterless),
    /// applies values through the DP store, attaches bindings, resolves
    /// resource references against application scope (missing keys fail
    /// loud, like inflation), wires handlers, lands Text, then parents
    /// children through Panel/ContentControl/Decorator hosts (anything else
    /// is a precise boundary, never silent).
    /// </summary>
    public object CreateInstance()
    {
        var type = Type ?? throw new InvalidOperationException(
            "FrameworkElementFactory.Type is not set.");
        if (!typeof(FrameworkElement).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"FrameworkElementFactory cannot create '{type.Name}': template visuals must derive FrameworkElement.");
        var instance = (FrameworkElement)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"'{type.Name}' has no parameterless constructor."));
        if (Name is not null)
        {
            var nameProperty = type.GetProperty("Name",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (nameProperty is not null && nameProperty.CanWrite && nameProperty.PropertyType == typeof(string))
                nameProperty.SetValue(instance, Name);
        }
        foreach (var (property, value) in _values)
            instance.SetValue(property, value);
        foreach (var (property, binding) in _bindings)
            BindingOperations.SetBinding(instance, property, binding);
        foreach (var (property, key) in _resources)
        {
            if (!instance.TryFindResource(key, out var resource) || resource is null)
                throw new InvalidOperationException(
                    $"SetResourceReference key '{key}' was not found in element, ancestor or application resources.");
            instance.SetValue(property, resource);
        }
        foreach (var (routedEvent, handler, handledToo) in _handlers)
            instance.AddHandler(routedEvent, handler, handledToo);
        if (Text is not null)
        {
            var textProperty = type.GetProperty("Text",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (textProperty is null || !textProperty.CanWrite || textProperty.PropertyType != typeof(string))
                throw new InvalidOperationException(
                    $"'{type.Name}' has no writable string Text property for factory text.");
            textProperty.SetValue(instance, Text);
        }
        foreach (var child in _children)
            AttachChild(instance, (FrameworkElement)child.CreateInstance());
        return instance;
    }

    private static void AttachChild(FrameworkElement parent, FrameworkElement child)
    {
        if (parent is Panel panel)
        {
            panel.Children.Add(child);
            return;
        }
        if (parent is ContentControl content)
        {
            if (content.Content is not null)
                throw new InvalidOperationException(
                    $"Factory child cannot land on '{parent.GetType().Name}': Content is already set.");
            content.Content = child;
            return;
        }
        if (parent is Decorator decorator)
        {
            if (decorator.Child is not null)
                throw new InvalidOperationException(
                    $"Factory child cannot land on '{parent.GetType().Name}': Child is already set.");
            decorator.Child = child;
            return;
        }
        // Border rides the Decorator branch above (engine Border derives
        // Decorator like live WPF, so the single-child rule is shared).
        throw new InvalidOperationException(
            $"Factory child cannot land on '{parent.GetType().Name}': only Panel, ContentControl, Decorator and Border hosts parent factory children in v1.");
    }

    private void ThrowIfSealed()
    {
        if (_sealed)
            throw new InvalidOperationException("The factory is sealed and cannot be modified.");
    }
}
public class DataTemplateSelector
{
    /// <summary>Chooses the item visual (base returns null; selectors override).</summary>
    public virtual DataTemplate? SelectTemplate(object item, DependencyObject container) => null;
}

/// <summary>Attached-property scope marker (matches
/// System.Windows.AttachedPropertyBrowsableForTypeAttribute).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AttachedPropertyBrowsableForTypeAttribute : Attribute
{
    public AttachedPropertyBrowsableForTypeAttribute(Type targetType) => TargetType = targetType;

    /// <summary>Controls the attached property may be set on.</summary>
    public Type TargetType { get; }
}

/// <summary>Uniform grid panel (matches
/// System.Windows.Controls.Primitives.UniformGrid): every visible child gets
/// an equal cell, row-major from FirstColumn, with WPF's auto rows/columns
/// fallback. Converted ScreenToGif ExtendedUniformGrid derives from this
/// (Columns/FirstColumn/IsReversed surface); layout routes through the
/// Size-typed MeasureOverride/ArrangeOverride hooks so converted overrides
/// bind, like the engine Adorner.</summary>
public class UniformGrid : Panel
{
    private int _columns;
    private int _rows;
    private int _firstColumn;

    /// <summary>Fixed column count (0 computes from rows/children).</summary>
    public int Columns
    {
        get => _columns;
        set { if (_columns == value) return; _columns = value; InvalidateMeasure(); }
    }

    /// <summary>Fixed row count (0 computes from columns/children).</summary>
    public int Rows
    {
        get => _rows;
        set { if (_rows == value) return; _rows = value; InvalidateMeasure(); }
    }

    /// <summary>First-cell column offset (clamped below Columns).</summary>
    public int FirstColumn
    {
        get => _firstColumn;
        set { if (_firstColumn == value) return; _firstColumn = value; InvalidateMeasure(); }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var measured = MeasureOverride(new Size(Slot(available.Width), Slot(available.Height)));
        return new WpfSize(measured.Width, measured.Height);
    }

    protected override void ArrangeCore(WpfRect finalRect) =>
        ArrangeOverride(new Size(Slot(finalRect.Width), Slot(finalRect.Height)));

    /// <summary>Default uniform measure (converted subclasses override).</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var (rows, cols) = ComputeCells();
        if (cols == 0 || rows == 0) return new Size(0, 0);
        var cell = new WpfSize(constraint.Width / cols, constraint.Height / rows);
        var maxWidth = 0.0;
        var maxHeight = 0.0;
        foreach (UIElement child in Children)
        {
            child.Measure(cell);
            maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }
        return new Size(maxWidth * cols, maxHeight * rows);
    }

    /// <summary>Default uniform arrange, row-major from FirstColumn (converted subclasses override).</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (rows, cols) = ComputeCells();
        if (cols == 0 || rows == 0 || Children.Count == 0) return finalSize;
        var cellWidth = finalSize.Width / cols;
        var cellHeight = finalSize.Height / rows;
        var x = cellWidth * FirstColumn;
        var y = 0.0;
        var right = Math.Max(0, finalSize.Width - 1.0);
        foreach (UIElement child in Children)
        {
            child.Arrange(new WpfRect(x, y, cellWidth, cellHeight));
            if (child.Visibility != Visibility.Collapsed)
            {
                x += cellWidth;
                if (x >= right)
                {
                    y += cellHeight;
                    x = 0.0;
                }
            }
        }
        return finalSize;
    }

    private (int Rows, int Cols) ComputeCells()
    {
        var first = FirstColumn;
        var columns = Columns;
        var rows = Rows;
        if (first >= columns && columns > 0) first = 0;
        if (rows != 0 && columns != 0) return (rows, columns);
        var count = 0;
        foreach (UIElement child in Children)
            if (child.Visibility != Visibility.Collapsed) count++;
        if (count == 0) count = 1;
        if (rows == 0)
        {
            if (columns > 0)
            {
                rows = (count + first + columns - 1) / columns;
            }
            else
            {
                rows = (int)Math.Sqrt(count);
                if (rows * rows < count) rows++;
                columns = rows;
            }
        }
        else if (columns == 0)
        {
            columns = (count + rows - 1) / rows;
        }
        return (rows, columns);
    }

    private static double Slot(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

    protected override void OnRender(DrawingContext dc)
    {
        foreach (UIElement child in Children) RenderVisible(dc, child);
    }
}

/// <summary>One deferred template value (internal inflation record): either a
/// TemplateBinding from the templated parent (Source, optional converter) or
/// an explicit {Binding} (TemplateSource, resolved live at inflation).</summary>
internal sealed class TemplateBindingEntry
{
    public TemplateBindingEntry(UIElement element, string target, string source)
    {
        Element = element;
        Target = target;
        Source = source;
    }

    /// <summary>Template element receiving the value.</summary>
    public UIElement Element { get; }

    /// <summary>Element property (plain "Text" or attached "A.B").</summary>
    public string Target { get; }

    /// <summary>Templated-parent property (plain, "Owner.Prop" or nested "A.B");
    /// empty when the value comes from TemplateSource.</summary>
    public string Source { get; }

    /// <summary>Converter key from {TemplateBinding P, Converter={StaticResource K}}.</summary>
    public string? ConverterKey { get; set; }

    /// <summary>Parse-resolved converter from Converter={x:Static ...} (singletons need no lookup).</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Resource parameter key from ConverterParameter={StaticResource K}.</summary>
    public string? ConverterParameterKey { get; set; }

    /// <summary>Literal ConverterParameter text (converters convert it, like
    /// string-form binding parameters).</summary>
    public string? ConverterParameter { get; set; }

    /// <summary>Explicit {Binding} (with RelativeSource/options); null for TemplateBinding.</summary>
    public Binding? TemplateSource { get; set; }

    /// <summary>Explicit template MultiBinding (ProgressBar.Clip and other
    /// composite template values); null unless the template property held one.</summary>
    /// <summary>Nested DynamicResource brush values (resolved per inflation).</summary>
    public TemplateDynamicBrushBinding? DynamicBrushBinding { get; set; }

    public MultiBinding? MultiSource { get; set; }

    /// <summary>Live multi-expression created at inflation (refreshed, never recreated).</summary>
    internal BindingExpressionBase? MultiLive { get; set; }

    /// <summary>Explicit template PriorityBinding (DatePicker thickness
    /// fallback chains); null unless the template property held one.</summary>
    public PriorityBinding? PrioritySource { get; set; }

    /// <summary>Live priority-expression created at inflation (refreshed, never recreated).</summary>
    internal BindingExpressionBase? PriorityLive { get; set; }

    /// <summary>Explicit template InputBindings (DialogHost click-away
    /// MouseBinding); null unless the template property held bindings.</summary>
    public List<TemplateInputBinding>? InputBindings { get; set; }

    /// <summary>Explicit template Effect binding (DialogHost background
    /// blur MultiBinding); null unless the template property held one.</summary>
    public TemplateEffectBinding? EffectBinding { get; set; }

    /// <summary>Explicit template brush-visual binding (DialogHost cover
    /// grid OpacityMask whose VisualBrush visual is a MultiBinding); null
    /// unless the template property held one.</summary>
    public TemplateBrushVisualBinding? BrushVisualBinding { get; set; }

    /// <summary>Explicit template mask binding (a bare MultiBinding under an
    /// OpacityMask template property whose vendor converter produces the mask
    /// Brush); null unless the template property held one.</summary>
    public TemplateMaskBinding? MaskBinding { get; set; }

    /// <summary>Whether the input bindings were added to the inflated
    /// element (re-resolves refresh parameters without duplicating).</summary>
    internal bool InputApplied { get; set; }

    /// <summary>Live expression created at inflation (refreshed, never recreated).</summary>
    public BindingExpression? Live { get; set; }

    /// <summary>Template-namescope visual for a VisualBrush Background (the
    /// Calendar slide copies mirror their month/year views); resolved at
    /// inflation from the fresh namescope, like ElementName bindings.</summary>
    public string? BrushVisualName { get; set; }

    /// <summary>Inline nested style from an element .Style property wrapper
    /// (the Clock readout TextBlocks); applied at inflation once the tree
    /// links, so template-local BasedOn resolves, like WPF.</summary>
    public Style? DeferredStyle { get; set; }

    /// <summary>Style resource key from a template Style={StaticResource Key}
    /// attribute; resolved at inflation against the linked tree (WPF resolves
    /// template resources at use, not in dictionary order, so sibling merges
    /// that land later still bind), failing loud when absent.</summary>
    public string? DeferredStyleKey { get; set; }

    /// <summary>Deferred Freezable binding (RotateTransform.Angle clock
    /// hands): a binding on a DP-less transform property, resolved at
    /// inflation and re-evaluated on source change, like WPF.</summary>
    public TemplateFreezableBinding? FreezableBinding { get; set; }

    /// <summary>Deferred Blend-behavior attach (an
    /// &lt;Interaction.Behaviors&gt; template property element); null unless
    /// the template property carried converted behaviors.</summary>
    public TemplateBehaviorAttach? BehaviorAttach { get; set; }

    /// <summary>Deferred grid-definition resource (a {DynamicResource ...}
    /// Row/ColumnDefinition dimension whose key missed at parse); resolved
    /// at inflation and refresh, like template StaticResource leniency.</summary>
    public TemplateDefinitionResource? DefinitionResource { get; set; }

    /// <summary>Deferred grid-definition binding (a {Binding ...}
    /// Row/ColumnDefinition dimension); resolved at inflation and refresh,
    /// like definition resources.</summary>
    public TemplateDefinitionBinding? DefinitionBinding { get; set; }
}

/// <summary>
/// Deferred grid-definition resource payload: the definition index within
/// its collection, the row/column side, the dimension kind and the resource
/// key. Resolution snapshots at inflation/refresh (live resource-change
/// push for definition dimensions stays a boundary).
/// </summary>
internal sealed class TemplateDefinitionResource
{
    public required int Index;
    public required bool IsRow;
    /// <summary>One of "Length", "Min" or "Max".</summary>
    public required string Dimension;
    public required object Key;
}

/// <summary>Deferred grid-definition binding payload (a {Binding} Width /
/// Height / Min / Max inside template grid definitions, like the DataGrid
/// frozen-column spacer): the definition index within its collection, the
/// row/column side, the dimension kind and the parsed binding. Sources
/// resolve like Freezable bindings (explicit Source, ElementName part,
/// TemplatedParent control, then the templated data context); resolution
/// snapshots at inflation/refresh, like definition resources.</summary>
internal sealed class TemplateDefinitionBinding
{
    public required int Index;
    public required bool IsRow;
    /// <summary>One of "Length", "Min" or "Max".</summary>
    public required string Dimension;
    public required Binding Binding;
}

/// <summary>
/// Deferred template effect binding (a MultiBinding under an Effect
/// template property whose vendor converter produces the Effect):
/// evaluated against the inflated element at inflation/refresh, like
/// other template bindings.
/// </summary>
internal sealed class TemplateEffectBinding
{
    public required MultiBinding Multi;
}

/// <summary>
/// Deferred template brush-visual binding (a VisualBrush under an
/// OpacityMask template property whose Visual is a MultiBinding):
/// the brush lands at parse while its visual evaluates against the
/// inflated element at inflation/refresh, like other template bindings.
/// </summary>
internal sealed class TemplateBrushVisualBinding
{
    public required VisualBrush Brush;
    public required MultiBinding Multi;
}

/// <summary>
/// Deferred template mask binding (a bare MultiBinding under an OpacityMask
/// template property whose vendor converter produces the mask Brush):
/// evaluated against the inflated element at inflation/refresh, like bound
/// template effects.
/// </summary>
internal sealed class TemplateMaskBinding
{
    public required MultiBinding Multi;
}

/// <summary>
/// Deferred template input binding (a MouseBinding/KeyBinding under an
/// InputBindings template property): the parsed binding plus the optional
/// {TemplateBinding ...} command-parameter source, resolved against the
/// templated control at inflation like TemplateSource.
/// </summary>
internal sealed class TemplateInputBinding
{
    public required InputBinding Binding;
    public string? CommandParameterSource;
}

/// <summary>
/// Deferred Blend-behavior attach payload (an
/// <Interaction.Behaviors> template property element): the converted
/// behaviors instantiate at parse, attach to the inflated host through the
/// converted Interaction.GetBehaviors collection once per inflation
/// (refresh re-resolves without re-attaching), and their {Binding}
/// attributes resolve through their own FreezableBinding entries, so the
/// attach runs before the first bound value lands, like WPF.
/// </summary>
public sealed class TemplateBehaviorAttach
{
    public required System.Reflection.MethodInfo GetBehaviors;
    public required List<object> Behaviors;
    internal bool Attached;
}

/// <summary>
/// Deferred Freezable binding payload (matches the {Binding} side of a
/// transform attribute): the transform instance of this build, its owning
/// template part, the CLR property and the parsed binding. Sources resolve
/// like template bindings (explicit Source, ElementName part, TemplatedParent
/// control, then the owner data context); values convert through the
/// binding converter plus CLR coercion and repaint the owner.
/// </summary>
public sealed class TemplateFreezableBinding
{
    public required object Target;
    public required FrameworkElement Owner;
    public required string Property;
    public Binding? Binding;
    /// <summary>Explicit template MultiBinding (bound geometry points combine
    /// several live sources); null unless the template property held one.</summary>
    public MultiBinding? Multi;
    /// <summary>True when pushes change layout (bound geometry re-measures
    /// its owner; transforms repaint only).</summary>
    public bool RemeasuresOwner;
    internal int Generation;
    internal object? SubscribedSource;
    internal bool DataContextHooked;
    internal bool SizeHooked;
    internal readonly HashSet<object> ChainHooked = new();
    internal bool Resolved;
}

/// <summary>One nested DynamicResource value inside a template brush
/// (the target is the brush/drawing instance, resolved at inflation).</summary>
public sealed class TemplateDynamicValue
{
    public required object Target;
    public required string Property;
    public required object Key;
}

/// <summary>Template brush with nested DynamicResource values (vendor thumb
/// tiles resolve their drawing brushes per inflation, like WPF).</summary>
public sealed class TemplateDynamicBrushBinding
{
    public required FrameworkElement Element;
    public required string Local;
    public required Brush Shell;
    public List<TemplateDynamicValue> Dynamics { get; } = new();
}

/// <summary>Template inflation engine (internal): ControlTemplate ownership,
/// VisualTree instantiation, TemplateBinding/{Binding} resolution and
/// trigger application through the loader/resource engine.
/// Ownership: the template holds the retained prototype (parse errors stay
/// load-time); every Apply builds a fresh instance tree so each control owns
/// its parts (FindName is per templated parent, like WPF).
/// Liveness: TemplateBinding values resolve at inflation and refresh when
/// the templated parent's dependency properties change, when its observed
/// CLR setters run, when the owning style re-applies, and once the loading
/// tree links (XAML attributes land after the implicit style applies).
/// Plain-CLR source changes with no engine hook stay a snapshot, like style
/// setters; EventTrigger entries stay a documented boundary.</summary>
internal static class TemplateApplier
{
    /// <summary>Resolves one deferred grid-definition resource against the
    /// live tree (element, theme, application and system scopes, like the
    /// other template resource arms); missing dynamic keys stay at their
    /// default until a refresh lands them.</summary>
    private static void ResolveDefinitionResource(Control control, TemplateBindingEntry entry, TemplateDefinitionResource definition)
    {
        if (entry.Element is not Grid grid) return;
        object? resource = null;
        var found = (control.TryFindResource(definition.Key, out resource) && resource is not null)
            || (ThemeDictionaries.TryFindThemeValue(control, definition.Key, out resource) && resource is not null)
            || (Application.Current is { } app && app.Resources.TryGetValue(definition.Key, out resource) && resource is not null)
            || (SystemTheme.TryFindSystemResource(definition.Key, out resource) && resource is not null);
        if (!found || resource is null) return;
        if (definition.IsRow)
        {
            if ((uint)definition.Index >= (uint)grid.RowDefinitions.Count) return;
            ApplyDefinitionValue(grid.RowDefinitions[definition.Index], definition.Dimension, resource, entry.Target);
        }
        else
        {
            if ((uint)definition.Index >= (uint)grid.ColumnDefinitions.Count) return;
            ApplyDefinitionValue(grid.ColumnDefinitions[definition.Index], definition.Dimension, resource, entry.Target);
        }
        grid.InvalidateMeasure();
    }

    /// <summary>Resolves one deferred grid-definition binding against the
    /// live tree (explicit Source, ElementName part, TemplatedParent control,
    /// then the templated data context, like Freezable bindings); missing
    /// ElementName parts fail loud, unresolvable sources without a
    /// FallbackValue keep the default, converters resolve with the same
    /// leniency as template binding converters, and values coerce through
    /// the shared definition path.</summary>
    private static void ResolveDefinitionBinding(Control control, TemplateBindingEntry entry, TemplateDefinitionBinding binding)
    {
        if (entry.Element is not Grid grid) return;
        var source = binding.Binding.EffectiveDirectSource(control)
            ?? (binding.Binding.ElementName is string name
                ? (control.ActiveTemplateNames.TryGetValue(name, out var part) ? part
                    : throw new InvalidOperationException(
                        $"Template grid definition binding ElementName '{name}' names no template part."))
                : null)
            ?? (binding.Binding.RelativeSource is not null ? BindingExpression.ResolveRelativeSource(control, binding.Binding.RelativeSource) : null)
            ?? control.DataContext;
        object? current = null;
        if (source is null || !BindingExpression.TryRead(source, binding.Binding.Path, out current))
        {
            if (binding.Binding.FallbackValue is null) return;
            current = binding.Binding.FallbackValue;
        }
        var converter = binding.Binding.Converter;
        if (converter is null && binding.Binding.DeferredConverterKey is { } key)
        {
            if ((!control.TryFindResource(key, out var resource) || resource is null) &&
                !ThemeDictionaries.TryFindThemeValue(control, key, out resource))
                return;
            if (resource is not IValueConverter keyed)
                throw new InvalidOperationException(
                    $"Template grid definition converter '{key}' is <{resource.GetType().Name}>, not an IValueConverter.");
            converter = keyed;
        }
        if (converter is not null)
        {
            current = converter.Convert(current,
                binding.Dimension == "Length" ? typeof(GridLength) : typeof(double),
                binding.Binding.ConverterParameter,
                System.Globalization.CultureInfo.InvariantCulture);
            if (current is null || ReferenceEquals(current, DependencyProperty.UnsetValue)) return;
        }
        object target = binding.IsRow
            ? ((uint)binding.Index < (uint)grid.RowDefinitions.Count ? grid.RowDefinitions[binding.Index] : null!)
            : ((uint)binding.Index < (uint)grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[binding.Index] : null!);
        if (target is null) return;
        ApplyDefinitionValue(target, binding.Dimension, current, entry.Target);
        grid.InvalidateMeasure();
    }

    /// <summary>Coerces a resolved definition resource to its dimension
    /// (GridLength for the length, double for min/max, like WPF).</summary>
    internal static void ApplyDefinitionValue(object definition, string dimension, object value, string display)
    {
        if (dimension == "Length")
        {
            var length = value switch
            {
                GridLength gl => gl,
                double d => new GridLength(d),
                int i => new GridLength(i),
                string text => GridLength.Parse(text),
                _ => throw new InvalidOperationException($"'{display}' resource does not convert to GridLength for grid definitions."),
            };
            if (definition is RowDefinition row) row.Height = length;
            else ((ColumnDefinition)definition).Width = length;
            return;
        }
        double number = value switch
        {
            double d => d,
            int i => i,
            string text => double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"'{display}' resource does not convert to double for grid definitions."),
        };
        if (definition is RowDefinition rowDef)
        {
            if (dimension == "Min") rowDef.MinHeight = number;
            else rowDef.MaxHeight = number;
        }
        else if (definition is ColumnDefinition columnDef)
        {
            if (dimension == "Min") columnDef.MinWidth = number;
            else columnDef.MaxWidth = number;
        }
    }

    /// <summary>Full (re)build: detaches the old tree, inflates fresh parts,
    /// wires, resolves, triggers, names and notifies. A null Template first
    /// resolves the converted default style (Themes/Generic.xaml by
    /// DefaultStyleKey): setting it re-enters Apply synchronously through
    /// OnTemplateChanged and inflates, so this frame returns either way.
    /// An already-applied Template is never rebuilt here (same-instance
    /// re-sets keep their tree; RefreshTemplate re-resolves).</summary>
    internal static void Apply(Control control)
    {
        if (control.Template is null)
        {
            // Converted default styles (Themes/Generic.xaml by DefaultStyleKey)
            // provide the Template: setting it re-enters Apply synchronously
            // through OnTemplateChanged and inflates, so this frame returns.
            // Without one, teardown keeps the previous null-Template behavior.
            if (!control.TemplateDefaulting)
            {
                control.TemplateDefaulting = true;
                try { ThemeDictionaries.ApplyDefaultStyle(control); }
                finally { control.TemplateDefaulting = false; }
                if (control.Template is not null) return;
            }
            Detach(control);
            return;
        }
        Detach(control);
        var template = control.Template;
        var built = template.Rebuild();
        control.ActiveTemplate = template;
        control.ActiveTemplateNames = built.Names;
        control.ActiveEntries = built.Entries;
        control.ActivePresenters = CollectPresenters(built.Root);
        control.TemplateRoot = built.Root;
        SetTemplateParents(built.Root, control);
        control.AddChild(built.Root);
        ResolveAll(control);
        Subscribe(control);
        control.RaiseOnApplyTemplate();
    }

    /// <summary>Re-resolves values and triggers on the live tree (no rebuild).</summary>
    internal static void RefreshTemplate(Control? control)
    {
        if (control?.ActiveTemplate is null || control.TemplateRoot is null || control.ActiveEntries is null)
            return;
        if (control.TemplateRefreshing) return;
        control.TemplateRefreshing = true;
        try { ResolveAll(control); }
        finally { control.TemplateRefreshing = false; }
    }

    /// <summary>Post-load pass: XAML attributes land after the implicit style
    /// applied its template, so every inflated template re-resolves once the
    /// tree links (converters and parent values are all present now).</summary>
    internal static void RefreshLoadedTree(UIElement root)
    {
        foreach (var control in Tree(root).OfType<Control>())
            if (control.ActiveTemplate is not null)
                ResolveAll(control);
    }

    private static void Detach(Control control)
    {
        if (control.TemplateRefreshHandler is not null)
        {
            control.PropertyChanged -= control.TemplateRefreshHandler;
            // SourceName conditions wired this handler to named parts: unhook
            // them too, or detached trees would retain the control (and fire
            // stale refreshes) after every rebuild.
            if (control.ActiveTemplate is not null && control.ActiveTemplateNames is not null)
            {
                foreach (var name in TemplateSourceNames(control.ActiveTemplate))
                {
                    if (control.ActiveTemplateNames.TryGetValue(name, out var found) &&
                        found is FrameworkElement part)
                        part.PropertyChanged -= control.TemplateRefreshHandler;
                }
            }
            control.TemplateRefreshHandler = null;
        }
        if (control.TemplateRoot is not null)
        {
            DetachTree(control.TemplateRoot);
            control.RemoveChild(control.TemplateRoot);
        }
        control.ActiveTemplate = null;
        control.ActiveTemplateNames = null;
        control.ActiveEntries = null;
        control.ActivePresenters = null;
        control.TriggerDirtied = null;
        control.TriggerActionsFired = null;
        control.TemplateRoot = null;
    }

    private static void Subscribe(Control control)
    {
        control.TemplateRefreshHandler = (_, _, _) => RefreshTemplate(control);
        control.PropertyChanged += control.TemplateRefreshHandler;
        // SourceName conditions track their named part, like WPF.
        if (control.ActiveTemplate is null) return;
        foreach (var name in TemplateSourceNames(control.ActiveTemplate))
        {
            if (control.ActiveTemplateNames?.TryGetValue(name, out var found) != true ||
                found is not FrameworkElement part)
                continue;
            part.PropertyChanged += control.TemplateRefreshHandler;
        }
    }

    /// <summary>Evaluates a SourceName trigger against its named template part (missing parts fail loud).</summary>
    private static bool IsSourcePartActive(Control control, string sourceName, Trigger trigger) =>
        IsSourcePartActive(control, sourceName, trigger.Property, trigger.Value);

    /// <summary>Evaluates a SourceName condition against its named template
    /// part (missing parts fail loud, like trigger parts).</summary>
    private static bool IsSourcePartActive(Control control, string sourceName, string property, object? value)
    {
        if (control.ActiveTemplateNames?.TryGetValue(sourceName, out var found) != true ||
            found is not FrameworkElement part)
            throw new InvalidOperationException(
                $"Trigger SourceName '{sourceName}' names no template part.");
        return TriggerEvaluation.IsPropertyActive(part, property, value);
    }

    /// <summary>All SourceName references in template triggers (plain
    /// triggers plus every MultiTrigger condition).</summary>
    private static IEnumerable<string> TemplateSourceNames(ControlTemplate template)
    {
        foreach (var trigger in template.Triggers.OfType<Trigger>())
            if (trigger.SourceName is { } name) yield return name;
        foreach (var multi in template.Triggers.OfType<MultiTrigger>())
            foreach (var condition in multi.Conditions)
                if (condition.SourceName is { } conditionName) yield return conditionName;
    }

    private static void ResolveAll(Control control)
    {
        RestoreDirtied(control);
        if (control.ActiveEntries is { } entries)
            foreach (var entry in entries)
                ResolveEntry(control, entry);
        if (control.ActivePresenters is { } presenters)
        {
            foreach (var presenter in presenters)
            {
                // An explicitly bound presenter keeps its TemplateBinding
                // value (gallery ControlExample samples); only content-less
                // presenters mirror the templated parent, like WPF.
                if (HasExplicitContent(control, presenter)) continue;
                // ContentSource (e.g. TabItem Header) redirects the mirror to
                // the named templated-parent property, like WPF.
                var content = presenter.ContentSource is { Length: > 0 } source &&
                    TryReadSource(control, source, out var sourced)
                    ? sourced
                    : ContentPresenter.ReadParentContent(control);
                if (!Equals(presenter.Content, content))
                    presenter.Content = content;
            }
        }
        ApplyTemplateTriggers(control);
    }

    private static bool HasExplicitContent(Control control, ContentPresenter presenter)
    {
        if (control.ActiveEntries is not { } entries) return false;
        foreach (var entry in entries)
            if (ReferenceEquals(entry.Element, presenter) && entry.Target == "Content")
                return true;
        return false;
    }

    private static void ResolveEntry(Control control, TemplateBindingEntry entry)
    {
        if (entry.BehaviorAttach is { } attach)
        {
            AttachTemplateBehaviors(entry, attach);
            return;
        }
        if (entry.FreezableBinding is { } freezable)
        {
            ApplyFreezableBinding(control, freezable);
            return;
        }
        if (entry.DynamicBrushBinding is { } dynamicBrush)
        {
            ApplyDynamicBrushBinding(control, dynamicBrush);
            return;
        }
        if (entry.DeferredStyle is { } deferred)
        {
            if (entry.Element is FrameworkElement styleHost)
                styleHost.Style = deferred;
            return;
        }
        if (entry.DeferredStyleKey is { } styleKey)
        {
            if (entry.Element is FrameworkElement keyedHost &&
                keyedHost.TryFindResource(styleKey, out var found) && found is Style keyedStyle)
                keyedHost.Style = keyedStyle;
            else
                throw new InvalidOperationException(
                    $"StaticResource '{styleKey}' was not found in element, ancestor or application resources (template scope).");
            return;
        }
        if (entry.BrushVisualName is not null)
        {
            // Template VisualBrush mirrors (the Calendar slide copies): the
            // brush landed at parse; its visual resolves from the inflated
            // namescope, like ElementName bindings (missing names fail loud).
            if (entry.Element is FrameworkElement host &&
                host.GetType().GetProperty("Background")?.GetValue(host) is VisualBrush visualBrush)
            {
                var names = control.ActiveTemplateNames;
                if (names is null || !names.TryGetValue(entry.BrushVisualName, out var found) || found is not Visual visual)
                    throw new InvalidOperationException($"Template VisualBrush visual '{entry.BrushVisualName}' was not found in the template namescope.");
                visualBrush.Visual = visual;
            }
            return;
        }
        if (entry.DefinitionResource is { } definitionResource)
        {
            ResolveDefinitionResource(control, entry, definitionResource);
            return;
        }
        if (entry.DefinitionBinding is { } definitionBinding)
        {
            ResolveDefinitionBinding(control, entry, definitionBinding);
            return;
        }
        if (entry.MultiSource is not null)
        {
            if (entry.MultiLive is null)
            {
                var multi = entry.MultiSource;
                ResolveEntryMultiConverter(control, multi);
                if (entry.Element is not FrameworkElement multiBindable)
                    return;
                // Owner-qualified composites (the RatingBar Ripple
                // TextBlock.Foreground MultiBinding): the dotted owner
                // name rides the attached transfer, like the single and
                // priority binding arms.
                entry.MultiLive = entry.Target.Contains('.')
                    ? new MultiBindingExpression(multiBindable, entry.Target, multi, attached: true)
                    : new MultiBindingExpression(multiBindable, entry.Target, multi);
            }
            else
            {
                entry.MultiLive.UpdateTarget();
            }
            return;
        }
        if (entry.PrioritySource is not null)
        {
            if (entry.PriorityLive is null)
            {
                var priority = entry.PrioritySource;
                if (entry.Element is not FrameworkElement priorityBindable)
                    return;
                entry.PriorityLive = entry.Target.Contains('.')
                    ? priorityBindable.SetAttachedBinding(entry.Target, priority)
                    : new PriorityBindingExpression(priorityBindable, entry.Target, priority);
            }
            else
            {
                entry.PriorityLive.UpdateTarget();
            }
            return;
        }
        if (entry.InputBindings is not null)
        {
            // Template click-away bindings (DialogHost MouseBinding):
            // TemplateBinding command parameters track the templated
            // control on every resolve; the bindings add once per
            // inflation, like WPF.
            if (entry.Element is not UIElement inputHost)
                return;
            foreach (var input in entry.InputBindings)
            {
                if (input.CommandParameterSource is not null
                    && TryReadSource(control, input.CommandParameterSource, out var parameter))
                    input.Binding.CommandParameter = parameter;
                if (!entry.InputApplied)
                    inputHost.InputBindings.Add(input.Binding);
            }
            entry.InputApplied = true;
            return;
        }
        if (entry.EffectBinding is not null)
        {
            // Bound template effects (DialogHost background blur): the
            // vendor converter produces the Effect from templated-parent
            // sources; re-evaluated on every resolve like live transfers.
            var effectMulti = entry.EffectBinding.Multi;
            ResolveEntryMultiConverter(control, effectMulti);
            if (entry.Element is FrameworkElement effectHost)
            {
                var produced = effectMulti.Evaluate(effectHost);
                effectHost.Effect = produced as Effect;
            }
            return;
        }
        if (entry.BrushVisualBinding is not null)
        {
            // Bound brush visuals (DialogHost cover-grid mask): the
            // MultiBinding picks the visual from templated-parent sources
            // on every resolve; the brush itself landed at parse.
            var brushVisual = entry.BrushVisualBinding;
            ResolveEntryMultiConverter(control, brushVisual.Multi);
            if (entry.Element is FrameworkElement brushHost)
            {
                var produced = brushVisual.Multi.Evaluate(brushHost);
                brushVisual.Brush.Visual = produced as Visual;
                brushHost.OpacityMask = brushVisual.Brush;
            }
            return;
        }
        if (entry.MaskBinding is not null)
        {
            // Bound template masks (MaterialDesign Card shadow mask): the
            // vendor converter produces the mask Brush from templated-parent
            // sources; re-evaluated on every resolve like live transfers.
            var maskMulti = entry.MaskBinding.Multi;
            ResolveEntryMultiConverter(control, maskMulti);
            if (entry.Element is FrameworkElement maskHost)
            {
                var produced = maskMulti.Evaluate(maskHost);
                maskHost.OpacityMask = produced as Brush;
            }
            return;
        }
        if (entry.TemplateSource is not null)
        {
            if (entry.Live is null)
            {
                var source = entry.TemplateSource;
                if (source.Converter is null && source.DeferredConverterKey is { } key)
                {
                    if (control.TryFindResource(key, out var resource) && resource is IValueConverter found)
                        source.Converter = found;
                    else if (ThemeDictionaries.TryFindThemeValue(control, key, out var themed) && themed is IValueConverter themedConverter)
                        source.Converter = themedConverter;
                }
                if (entry.Element is not FrameworkElement bindable)
                    return;
                entry.Live = entry.Target.Contains('.')
                    ? bindable.SetAttachedBinding(entry.Target, source)
                    : bindable.SetBinding(entry.Target, source);
            }
            else
            {
                entry.Live.UpdateTarget();
            }
            return;
        }
        if (!TryReadSource(control, entry.Source, out var value))
            throw new InvalidOperationException(
                $"TemplateBinding '{entry.Source}' names no property of <{control.GetType().Name}>.");
        if (entry.ConverterKey is not null || entry.Converter is not null)
        {
            // Missing converter keys stay unset until they appear (same
            // load-order leniency as template StaticResource); mistyped keys fail loud.
            // Theme dictionaries are the fallback scope (generic.xaml converters
            // resolve against the theme dictionary itself, like WPF).
            var converter = entry.Converter;
            if (converter is null)
            {
                if ((!control.TryFindResource(entry.ConverterKey, out var resource) || resource is null) &&
                    !ThemeDictionaries.TryFindThemeValue(control, entry.ConverterKey, out resource))
                    return;
                if (resource is not IValueConverter keyed)
                    throw new InvalidOperationException(
                        $"TemplateBinding converter '{entry.ConverterKey}' is <{resource.GetType().Name}>, not an IValueConverter.");
                converter = keyed;
            }
            object? parameter = entry.ConverterParameter;
            if (entry.ConverterParameterKey is not null)
            {
                // Missing parameter keys stay unset until they appear (same
                // load-order leniency as converter keys above).
                if ((!control.TryFindResource(entry.ConverterParameterKey, out var presource) || presource is null) &&
                    !ThemeDictionaries.TryFindThemeValue(control, entry.ConverterParameterKey, out presource))
                    return;
                parameter = presource;
            }
            value = converter.Convert(value, TargetTypeOf(entry), parameter,
                System.Globalization.CultureInfo.InvariantCulture);
            if (ReferenceEquals(value, Binding.DoNothing))
                return;
        }
        SetTarget(entry, value);
    }

    /// <summary>Resolves a template MultiBinding StaticResource converter
    /// against the templated control scope (theme fallback, like the
    /// composite transfer arm).</summary>
    private static void ResolveEntryMultiConverter(Control control, MultiBinding multi)
    {
        if (multi.Converter is null && multi.DeferredConverterKey is { } key)
        {
            if (control.TryFindResource(key, out var resource) && resource is IMultiValueConverter found)
                multi.Converter = found;
            else if (ThemeDictionaries.TryFindThemeValue(control, key, out var themed) && themed is IMultiValueConverter themedConverter)
                multi.Converter = themedConverter;
        }
    }

    private static Type TargetTypeOf(TemplateBindingEntry entry)
    {
        if (entry.Target.Contains('.'))
            return typeof(object);
        var property = entry.Element.GetType().GetProperty(entry.Target,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property is not null) return property.PropertyType;
        var dp = DependencyProperty.FromName(entry.Element.GetType(), entry.Target);
        if (dp is not null) return dp.PropertyType;
        return typeof(object);
    }

    /// <summary>Reads a templated-parent source (owner-qualified DP, nested
    /// CLR path, plain CLR/DP, then the attached bag).</summary>
    internal static bool TryReadSource(FrameworkElement parent, string source, out object? value)
    {
        // BAML keeps the xmlns prefix on TemplateBinding sources
        // (wpf:TextFieldAssist.TextFieldCornerRadius): strip it so the
        // owner resolves like the XAML namescope does.
        var colon = source.IndexOf(':');
        var dotFirst = source.IndexOf('.');
        if (colon > 0 && colon < dotFirst)
            source = source[(colon + 1)..];
        var dot = source.IndexOf('.');
        if (dot > 0)
        {
            var owner = XamlLoader.FindEngineType(source[..dot])
                ?? XamlLoader.FindConvertedType(source[..dot])
                ?? FindLoadedConvertedType(source[..dot]);
            if (owner is not null)
            {
                var dp = DependencyProperty.FromName(owner, source[(dot + 1)..]);
                if (dp is not null)
                {
                    value = parent.GetValue(dp);
                    return true;
                }
                var qualified = owner.GetProperty(source[(dot + 1)..],
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (qualified?.CanRead == true)
                {
                    value = qualified.GetValue(parent);
                    return true;
                }
            }
            else if (TryReadNested(parent, source, out value))
            {
                return true;
            }
            value = parent.GetAttached(source);
            return value is not null;
        }
        var info = parent.GetType().GetProperty(source,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (info?.CanRead == true)
        {
            value = info.GetValue(parent);
            return true;
        }
        var direct = DependencyProperty.FromName(parent.GetType(), source);
        if (direct is not null)
        {
            value = parent.GetValue(direct);
            return true;
        }
        value = parent.GetAttached(source);
        return value is not null;
    }

    /// <summary>Scans loaded converted assemblies for an owner short name
    /// (the staged entry first via FindConvertedType, then a unique
    /// AppDomain scan excluding the engine assembly, mirroring the BAML
    /// setter-owner resolution; XAML names only visible types, so internal
    /// same-named helpers never shadow, and ambiguous visible matches keep
    /// the old miss. When the requested static member is known (x:Static),
    /// it disambiguates: same-named types without that public static member
    /// never shadow the unique holder, while genuinely ambiguous holders
    /// keep the old miss).</summary>
    internal static Type? FindLoadedConvertedType(string shortName, string? memberName = null)
    {
        Type? found = null;
        var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, typeof(FrameworkElement).Assembly)) continue;
            Type? candidate;
            try { candidate = WpfAppBoot.SafeGetTypes(assembly).FirstOrDefault(t => t.Name == shortName && t.IsVisible); }
            catch { continue; }
            if (candidate is null || ReferenceEquals(found, candidate)) continue;
            if (memberName is not null && !ExposesStaticMember(candidate, memberName, flags)) continue;
            if (found is not null) return null;
            found = candidate;
        }
        return found;
    }

    private static bool ExposesStaticMember(Type candidate, string memberName, BindingFlags flags) =>
        candidate.GetProperty(memberName, flags)?.CanRead == true || candidate.GetField(memberName, flags) is not null;

    private static bool TryReadNested(object root, string path, out object? value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value is null) return false;
            var info = value.GetType().GetProperty(segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (info?.CanRead != true) return false;
            value = info.GetValue(value);
        }
        return true;
    }

    /// <summary>Assigns an inflated value (CLR, same-named DP, owner DP or
    /// attached bag, mirroring setter write order); equal values skip.
    /// A runtime transfer whose type cannot land (e.g. a Thickness
    /// BorderThickness onto a double StrokeThickness, where real WPF's
    /// TemplateBinding also performs no conversion) keeps the target's
    /// current value instead of failing the tree, matching WPF's binding
    /// error semantics; XAML literal strings still fail loud at load.</summary>
    /// <summary>Attaches parsed Blend behaviors to the inflated template
    /// part through the converted Interaction.GetBehaviors collection (once
    /// per inflation; refresh re-resolves without re-attaching, like the
    /// WPF attach-once behavior-collection lifetime).</summary>
    private static void AttachTemplateBehaviors(TemplateBindingEntry entry, TemplateBehaviorAttach attach)
    {
        if (attach.Attached) return;
        attach.Attached = true;
        System.Collections.IList collection;
        try
        {
            collection = (System.Collections.IList)attach.GetBehaviors.Invoke(null, [entry.Element])!;
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or NullReferenceException)
        {
            throw new InvalidOperationException(
                $"Interaction.GetBehaviors failed on <{entry.Element.GetType().Name}> ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
        }
        if (collection is null)
            throw new InvalidOperationException(
                $"Interaction.GetBehaviors returned null on <{entry.Element.GetType().Name}> (converted shape changed).");
        foreach (var behavior in attach.Behaviors)
        {
            try
            {
                collection.Add(behavior);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"<Interaction.Behaviors> cannot hold <{behavior.GetType().Name}> ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }

    private static void ApplyFreezableBinding(Control control, TemplateFreezableBinding fb)
    {
        if (fb.Multi is { } multi)
        {
            ApplyFreezableMulti(control, fb, multi);
            return;
        }
        if (fb.Binding is { } binding)
        {
            ApplyFreezableSingle(control, fb, binding);
            return;
        }
        throw new InvalidOperationException($"Template {fb.Target.GetType().Name}.{fb.Property} has neither Binding nor MultiBinding.");
    }

    private static void ApplyFreezableSingle(Control control, TemplateFreezableBinding fb, Binding binding)
    {
        var converter = binding.Converter;
        if (converter is null && binding.DeferredConverterKey is { } key)
        {
            if ((!control.TryFindResource(key, out var resource) || resource is null) &&
                !ThemeDictionaries.TryFindThemeValue(control, key, out resource))
                throw new InvalidOperationException(
                    $"Template converter '{key}' is not an IValueConverter (template scope).");
            if (resource is not IValueConverter keyed)
                throw new InvalidOperationException(
                    $"Template converter '{key}' is <{resource.GetType().Name}>, not an IValueConverter.");
            converter = keyed;
        }
        object? source = binding.EffectiveDirectSource(control);
        source ??= binding.ElementName is string name
            ? (control.ActiveTemplateNames?.TryGetValue(name, out var found) == true ? found
                : throw new InvalidOperationException(
                    $"Template {fb.Target.GetType().Name}.{fb.Property} binding ElementName '{name}' names no template part."))
            : null;
        if (source is null && binding.RelativeSource is not null)
        {
            var relative = binding.RelativeSource;
            source = relative.Mode switch
            {
                RelativeSourceMode.TemplatedParent => control,
                RelativeSourceMode.Self => fb.Target,
                RelativeSourceMode.FindAncestor when relative.AncestorType is not null =>
                    BindingExpression.FindAncestorElement(fb.Owner, relative.AncestorType, relative.AncestorLevel)
                    ?? throw new InvalidOperationException(
                        $"Template {fb.Target.GetType().Name}.{fb.Property} binding finds no ancestor {relative.AncestorType.Name}."),
                RelativeSourceMode.FindAncestor =>
                    throw new InvalidOperationException(
                        $"Template {fb.Target.GetType().Name}.{fb.Property} binding FindAncestor needs AncestorType=."),
                _ => null,
            };
        }
        source ??= (object?)fb.Owner.DataContext;
        var pathText = binding.Path.Path;
        var firstSegment = pathText.Split('.')[0];
        fb.Generation++;
        var gen = fb.Generation;
        void Push()
        {
            if (gen != fb.Generation) return;
            if (source is null) return;
            if (!BindingExpression.TryRead(source, pathText, out var current)) return;
            if (converter is not null)
            {
                var converted = converter.Convert(current, typeof(object), binding.ConverterParameter, System.Globalization.CultureInfo.InvariantCulture);
                if (ReferenceEquals(converted, Binding.DoNothing)) return;
                current = converted;
            }
            var prop = fb.Target.GetType().GetProperty(fb.Property,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true)
                throw new InvalidOperationException(
                    $"'{fb.Target.GetType().Name}' has no writable property '{fb.Property}'.");
            object? final = current;
            if (final is string text)
                final = StyleValueConverter.Convert(text, prop.PropertyType)
                    ?? throw new InvalidOperationException(
                        $"'{text}' is not a {prop.PropertyType.Name} for {fb.Target.GetType().Name}.{fb.Property}.");
            else if (final is not null && !prop.PropertyType.IsInstanceOfType(final))
            {
                try { final = Convert.ChangeType(final, prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
                {
                    throw new InvalidOperationException(
                        $"'{final}' is not a {prop.PropertyType.Name} for {fb.Target.GetType().Name}.{fb.Property}.");
                }
            }
            prop.SetValue(fb.Target, final);
            fb.Resolved = true;
            if (fb.RemeasuresOwner) fb.Owner.InvalidateMeasure();
            fb.Owner.InvalidateVisual();
        }
        Push();
        if (binding.Mode == BindingMode.OneTime) return;
        HookFreezableSource(fb, source, pathText, gen, Push);
        HookFreezableSize(fb, gen, Push);
        if (!ReferenceEquals(fb.SubscribedSource, source))
        {
            fb.SubscribedSource = source;
            if (source is System.ComponentModel.INotifyPropertyChanged inpc)
                inpc.PropertyChanged += (_, e) =>
                {
                    if (gen != fb.Generation) return;
                    if (e.PropertyName is not null && e.PropertyName != firstSegment) return;
                    Push();
                };
            if (source is DependencyObject dependency)
                dependency.PropertyChanged += (dp, _, _) =>
                {
                    if (gen != fb.Generation) return;
                    if (dp.Name != firstSegment) return;
                    Push();
                };
        }
        if (!fb.DataContextHooked)
        {
            fb.DataContextHooked = true;
            fb.Owner.DataContextChanged += (_, _) =>
            {
                if (gen != fb.Generation) return;
                if (binding.Mode == BindingMode.OneTime && fb.Resolved) return;
                ApplyFreezableBinding(control, fb);
            };
        }
    }

    /// <summary>Pushes one template MultiBinding onto a DP-less target
    /// property (bound geometry points combine live sources, like the
    /// Clock-hand singles above but evaluated per child).</summary>
    private static void ApplyFreezableMulti(Control control, TemplateFreezableBinding fb, MultiBinding multi)
    {
        fb.Generation++;
        var gen = fb.Generation;
        void Push()
        {
            if (gen != fb.Generation) return;
            var combined = EvaluateTemplateMulti(control, fb, multi);
            if (combined is null || ReferenceEquals(combined, DependencyProperty.UnsetValue)) return;
            var prop = fb.Target.GetType().GetProperty(fb.Property,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true)
                throw new InvalidOperationException(
                    $"'{fb.Target.GetType().Name}' has no writable property '{fb.Property}'.");
            object? final = combined;
            if (final is string text)
                final = StyleValueConverter.Convert(text, prop.PropertyType)
                    ?? throw new InvalidOperationException(
                        $"'{text}' is not a {prop.PropertyType.Name} for {fb.Target.GetType().Name}.{fb.Property}.");
            else if (final is not null && !prop.PropertyType.IsInstanceOfType(final))
            {
                try { final = Convert.ChangeType(final, prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
                {
                    throw new InvalidOperationException(
                        $"'{final}' is not a {prop.PropertyType.Name} for {fb.Target.GetType().Name}.{fb.Property}.");
                }
            }
            prop.SetValue(fb.Target, final);
            fb.Resolved = true;
            if (fb.RemeasuresOwner) fb.Owner.InvalidateMeasure();
            fb.Owner.InvalidateVisual();
        }
        Push();
        if (multi.Mode == BindingMode.OneTime) return;
        foreach (var childBase in multi.Bindings)
        {
            if (childBase is not Binding child) continue;
            var source = ResolveTemplateChildSource(control, fb, child);
            HookFreezableSource(fb, source, child.Path.Path, gen, Push);
        }
        HookFreezableSize(fb, gen, Push);
        if (!fb.DataContextHooked)
        {
            fb.DataContextHooked = true;
            fb.Owner.DataContextChanged += (_, _) =>
            {
                if (gen != fb.Generation) return;
                if (multi.Mode == BindingMode.OneTime && fb.Resolved) return;
                ApplyFreezableBinding(control, fb);
            };
        }
    }

    /// <summary>Evaluates a template MultiBinding with template scope:
    /// explicit sources (deferred keys included), ElementName parts from
    /// the inflated namescope, TemplatedParent/ancestor relative sources,
    /// then the owner data context, like Freezable singles.</summary>
    private static object? EvaluateTemplateMulti(Control control, TemplateFreezableBinding fb, MultiBinding multi)
    {
        var values = new object?[multi.Bindings.Count];
        for (var i = 0; i < multi.Bindings.Count; i++)
        {
            if (multi.Bindings[i] is not Binding child) return null;
            var source = ResolveTemplateChildSource(control, fb, child);
            if (source is null || !BindingExpression.TryRead(source, child.Path, out var value))
            {
                if (child.FallbackValue is not null) value = child.FallbackValue;
                else return null;
            }
            else if (value is null)
            {
                var childConverter = ResolveTemplateChildConverter(control, child);
                if (childConverter is not null)
                    value = childConverter.Convert(null, typeof(object), child.ConverterParameter, child.ConverterCulture);
                if (value is null && child.TargetNullValue is not null)
                    value = child.TargetNullValue;
            }
            else if (ResolveTemplateChildConverter(control, child) is { } childConverter)
                value = childConverter.Convert(value, typeof(object), child.ConverterParameter, child.ConverterCulture);
            values[i] = value;
        }
        var converter = multi.Converter;
        if (converter is not null)
            return converter.Convert(values, typeof(object), multi.ConverterParameter, multi.ConverterCulture);
        if (multi.StringFormat is { } format)
        {
            if (format.StartsWith("{}", StringComparison.Ordinal)) format = format[2..];
            return string.Format(multi.ConverterCulture, format, values);
        }
        return values.Length == 1 ? values[0] : string.Concat(values.Select(v => v?.ToString()));
    }

    /// <summary>Resolves one deferred child converter against the control
    /// scope (single bindings resolve inline; shared here for multis).</summary>
    private static IValueConverter? ResolveTemplateChildConverter(Control control, Binding child)
    {
        if (child.Converter is not null) return child.Converter;
        if (child.DeferredConverterKey is { } key)
        {
            if (control.TryFindResource(key, out var resource) && resource is IValueConverter found)
                return found;
            if (ThemeDictionaries.TryFindThemeValue(control, key, out var themed) && themed is IValueConverter themedConverter)
                return themedConverter;
        }
        return null;
    }

    /// <summary>Resolves one template binding source with template scope
    /// (deferred resource keys, namescope parts, relative sources, then the
    /// owner data context).</summary>
    private static object? ResolveTemplateChildSource(Control control, TemplateFreezableBinding fb, Binding child)
    {
        var source = child.EffectiveDirectSource(control);
        source ??= child.ElementName is string name
            ? (control.ActiveTemplateNames?.TryGetValue(name, out var found) == true ? found
                : throw new InvalidOperationException(
                    $"Template {fb.Target.GetType().Name}.{fb.Property} binding ElementName '{name}' names no template part."))
            : null;
        if (source is null && child.RelativeSource is not null)
        {
            var relative = child.RelativeSource;
            source = relative.Mode switch
            {
                RelativeSourceMode.TemplatedParent => control,
                RelativeSourceMode.Self => fb.Target,
                RelativeSourceMode.FindAncestor when relative.AncestorType is not null =>
                    BindingExpression.FindAncestorElement(fb.Owner, relative.AncestorType, relative.AncestorLevel)
                    ?? throw new InvalidOperationException(
                        $"Template {fb.Target.GetType().Name}.{fb.Property} binding finds no ancestor {relative.AncestorType.Name}."),
                RelativeSourceMode.FindAncestor =>
                    throw new InvalidOperationException(
                        $"Template {fb.Target.GetType().Name}.{fb.Property} binding FindAncestor needs AncestorType=."),
                _ => null,
            };
        }
        return source ?? fb.Owner.DataContext;
    }

    /// <summary>Subscribes a Freezable push to its source head (filtered by
    /// the first path segment, like the original pump) plus every deeper
    /// path node (dotted proxy paths re-push when an intermediate value
    /// changes), and to layout changes on element sources.</summary>
    private static void HookFreezableSource(TemplateFreezableBinding fb, object? source, string pathText, int gen, Action push)
    {
        if (source is null) return;
        var segments = pathText.Split('.');
        HookFreezableNode(fb, source, segments.Length == 0 ? null : segments[0], gen, push);
        var prefix = source;
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (prefix is null) break;
            if (!BindingExpression.TryRead(prefix, segments[i], out var next)) break;
            prefix = next;
            HookFreezableNode(fb, prefix, null, gen, push);
        }
        if (!ReferenceEquals(fb.SubscribedSource, source)) fb.SubscribedSource = source;
    }

    /// <summary>Subscribes one push to a single source object (change
    /// notifications filtered, layout unfiltered); each object hooks once
    /// per entry, and stale generations no-op, like the original pump.</summary>
    private static void HookFreezableNode(TemplateFreezableBinding fb, object? node, string? filter, int gen, Action push)
    {
        if (node is null || !fb.ChainHooked.Add(node)) return;
        if (node is System.ComponentModel.INotifyPropertyChanged inpc)
            inpc.PropertyChanged += (_, e) =>
            {
                if (gen != fb.Generation) return;
                if (filter is not null && e.PropertyName is not null && e.PropertyName != filter) return;
                push();
            };
        if (node is DependencyObject dependency)
            dependency.PropertyChanged += (dp, _, _) =>
            {
                if (gen != fb.Generation) return;
                if (filter is not null && dp.Name != filter) return;
                push();
            };
        if (node is FrameworkElement fe)
            fe.SizeChanged += (_, _) =>
            {
                if (gen != fb.Generation) return;
                push();
            };
    }

    /// <summary>Re-pushes a Freezable value when its owner re-lays-out
    /// (geometry bound to ActualWidth follows resizes, like multis).</summary>
    private static void HookFreezableSize(TemplateFreezableBinding fb, int gen, Action push)
    {
        if (fb.SizeHooked) return;
        fb.SizeHooked = true;
        fb.Owner.SizeChanged += (_, _) =>
        {
            if (gen != fb.Generation) return;
            push();
        };
    }

    /// <summary>Resolves one template brush with nested DynamicResource
    /// values (missing keys leave the placeholder, like other template
    /// resource landings; refresh re-resolves).</summary>
    private static void ApplyDynamicBrushBinding(Control control, TemplateDynamicBrushBinding binding)
    {
        foreach (var dynamic in binding.Dynamics)
            ResolveDynamicBrushValue(control, binding, dynamic);
        foreach (var dynamic in binding.Dynamics)
        {
            var captured = dynamic;
            Application.DynamicResourceManager.SubscribeProperty(binding.Element,
                "template-dynamic-brush:" + captured.Key, captured.Key, (_, resolved) =>
                {
                    if (resolved is null) return;
                    SetDynamicBrushValue(captured, resolved);
                });
        }
    }

    private static void ResolveDynamicBrushValue(Control control, TemplateDynamicBrushBinding binding, TemplateDynamicValue dynamic)
    {
        if (control.TryFindResource(dynamic.Key, out var resource) && resource is not null)
            SetDynamicBrushValue(dynamic, resource);
        else if (binding.Element.TryFindResource(dynamic.Key, out var scoped) && scoped is not null)
            SetDynamicBrushValue(dynamic, scoped);
    }

    /// <summary>Sets one nested brush value (DP preferred, CLR fallback;
    /// mistyped resolutions keep the placeholder, like refresh).</summary>
    private static void SetDynamicBrushValue(TemplateDynamicValue dynamic, object? value)
    {
        if (value is null) return;
        var type = dynamic.Target.GetType();
        var dp = DependencyProperty.FromName(type, dynamic.Property);
        if (dp is not null && dynamic.Target is DependencyObject target)
        {
            if (dp.PropertyType.IsInstanceOfType(value)) target.SetValue(dp, value);
            return;
        }
        var prop = type.GetProperty(dynamic.Property,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
        if (prop?.CanWrite == true && prop.SetMethod?.IsPublic == true &&
            (value is null || prop.PropertyType.IsInstanceOfType(value)))
            prop.SetValue(dynamic.Target, value);
    }

    internal static void SetTarget(TemplateBindingEntry entry, object? value)
    {
        try
        {
            SetTargetValue(entry.Element, entry.Target, value);
        }
        catch (InvalidOperationException) when (value is not string)
        {
        }
    }

    private static void SetTargetValue(UIElement element, string target, object? value)
    {
        if (target.Contains('.'))
        {
            var dot = target.IndexOf('.');
            var owner = XamlLoader.FindEngineType(target[..dot]);
            var dp = owner is not null ? DependencyProperty.FromName(owner, target[(dot + 1)..]) : null;
            if (dp is not null && element is DependencyObject dependency)
            {
                var coerced = Coerce(value, dp.PropertyType, target);
                if (Equals(dependency.GetValue(dp), coerced)) return;
                dependency.SetValue(dp, coerced);
                return;
            }
            if (Equals(element.GetAttached(target), value)) return;
            element.SetAttached(target, value!);
            return;
        }
        var property = element.GetType().GetProperty(target,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property is not null && property.CanWrite)
        {
            var converted = ConvertValue(value, property.PropertyType, target);
            if (Equals(property.GetValue(element), converted)) return;
            property.SetValue(element, converted);
            return;
        }
        var direct = DependencyProperty.FromName(element.GetType(), target);
        if (direct is not null && element is DependencyObject directTarget)
        {
            var coerced = Coerce(value, direct.PropertyType, target);
            if (Equals(directTarget.GetValue(direct), coerced)) return;
            directTarget.SetValue(direct, coerced);
            return;
        }
        throw new InvalidOperationException(
            $"'{target}' is not a settable property of template <{element.GetType().Name}>.");
    }

    private static object? ConvertValue(object? value, Type propertyType, string target)
    {
        if (value is null)
        {
            if (propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) is null)
                throw new InvalidOperationException(
                    $"'{target}' cannot take null (needs {propertyType.Name}).");
            return null;
        }
        if (propertyType.IsInstanceOfType(value)) return value;
        if (value is string text)
        {
            try { return StyleValueConverter.Convert(text, propertyType); }
            catch (FormatException ex) { throw new InvalidOperationException(ex.Message); }
            catch (ArgumentException ex) { throw new InvalidOperationException(ex.Message); }
            catch (InvalidOperationException ex) { throw new InvalidOperationException(ex.Message); }
        }
        throw new InvalidOperationException(
            $"'{value}' is not a {propertyType.Name} for template '{target}'.");
    }

    private static object? Coerce(object? value, Type propertyType, string target) =>
        Setter.CoerceResourceValue(value, propertyType, "template:" + target);

    private static void ApplyTemplateTriggers(Control control)
    {
        var template = control.ActiveTemplate;
        if (template is null || template.Triggers.Count == 0) return;
        var names = control.ActiveTemplateNames;
        var context = new StyleApplyContext { SkipEventWires = true, Source = BaseValueSource.TemplateTrigger };
        UIElement scopeRoot = control.TemplateRoot ?? control;
        var scope = scopeRoot as FrameworkElement ?? control;
        foreach (var triggerBase in template.Triggers)
        {
            if (!IsTemplateTriggerActive(control, scope, triggerBase)) continue;
            ApplyTriggerSetters(control, names, TemplateTriggerSetters(triggerBase), context);
            // Enter-actions fire once per activation (not every refresh), like WPF.
            if (triggerBase.EnterActions.Count > 0 &&
                (control.TriggerActionsFired ??= new HashSet<TriggerBase>()).Add(triggerBase))
                RunTriggerActions(scope, triggerBase.EnterActions);
        }
    }

    /// <summary>Evaluates data-template triggers on an inflated visual
    /// (DataTrigger/MultiDataTrigger against the item, TargetName setters
    /// onto named parts, like WPF). Runs once at inflation and again on
    /// every DataContext change, when the item arrives; pre-trigger values
    /// restore before each pass. Live source edits between DataContext
    /// changes stay a precise boundary.</summary>
    internal static void ApplyDataTemplateTriggers(UIElement root, IReadOnlyDictionary<string, object> names, TriggerCollection triggers)
    {
        if (triggers.Count == 0) return;
        if (root is not FrameworkElement scope)
            throw new InvalidOperationException("DataTemplate.Triggers need a FrameworkElement root.");
        EvaluateDataTemplateTriggers(scope, names, triggers);
        scope.DataContextChanged += (_, _) => EvaluateDataTemplateTriggers(scope, names, triggers);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, List<DataTemplateDirtied>> DataTemplateDirtiedTable = new();

    private sealed class DataTemplateDirtied(FrameworkElement Target, string Key, object? Previous)
    {
        public FrameworkElement Target = Target;
        public string Key = Key;
        public object? Previous = Previous;
    }

    private static void EvaluateDataTemplateTriggers(FrameworkElement root, IReadOnlyDictionary<string, object> names, TriggerCollection triggers)
    {
        if (DataTemplateDirtiedTable.TryGetValue(root, out var dirtied))
        {
            DataTemplateDirtiedTable.Remove(root);
            foreach (var dirty in dirtied)
                SetTargetValue(dirty.Target, dirty.Key, dirty.Previous);
        }
        var context = new StyleApplyContext { SkipEventWires = true, Source = BaseValueSource.TemplateTrigger };
        foreach (var triggerBase in triggers)
        {
            var active = triggerBase switch
            {
                DataTrigger data => data.Binding is not null &&
                    TriggerEvaluation.IsBindingActive(root, data.Binding, data.Value),
                MultiDataTrigger multi => multi.Conditions.All(c =>
                    c.Binding is not null && TriggerEvaluation.IsBindingActive(root, c.Binding, c.Value)),
                _ => false,
            };
            if (!active) continue;
            SetterBaseCollection? setters = triggerBase switch
            {
                DataTrigger data => data.Setters,
                MultiDataTrigger multi => multi.Setters,
                _ => null,
            };
            if (setters is null) continue;
            foreach (var setterBase in setters)
            {
                if (setterBase is not Setter setter) continue;
                FrameworkElement part = root;
                if (setter.TargetName is not null)
                {
                    if (!names.TryGetValue(setter.TargetName, out var found) || found is not FrameworkElement named)
                        throw new InvalidOperationException(
                            $"Trigger TargetName '{setter.TargetName}' names no data-template part.");
                    part = named;
                }
                var key = setter.AttachedProperty ?? setter.Property?.Name;
                if (key is not null)
                {
                    var table = DataTemplateDirtiedTable.GetOrCreateValue(root);
                    if (!table.Any(d => ReferenceEquals(d.Target, part) && d.Key == key))
                        table.Add(new DataTemplateDirtied(part, key, ReadCurrent(part, key)));
                }
                setter.ApplyToTemplatePart(part, context);
            }
        }
    }

    private static bool IsTemplateTriggerActive(Control control, FrameworkElement scope, TriggerBase triggerBase) =>
        triggerBase switch
        {
            Trigger trigger when trigger.SourceName is { } source =>
                IsSourcePartActive(control, source, trigger),
            Trigger trigger => TriggerEvaluation.IsPropertyActive(control, trigger.Property, trigger.Value),
            MultiTrigger multi => multi.Conditions.All(c =>
                c.SourceName is { } source
                    ? IsSourcePartActive(control, source, c.Property ?? string.Empty, c.Value)
                    : c.Property is not null && TriggerEvaluation.IsPropertyActive(control, c.Property, c.Value)),
            DataTrigger data => data.Binding is not null &&
                TriggerEvaluation.IsBindingActive(scope, data.Binding, data.Value),
            MultiDataTrigger multiData => multiData.Conditions.All(c =>
                c.Binding is not null && TriggerEvaluation.IsBindingActive(scope, c.Binding, c.Value)),
            _ => false,
        };

    private static SetterBaseCollection TemplateTriggerSetters(TriggerBase triggerBase) =>
        triggerBase switch
        {
            Trigger trigger => trigger.Setters,
            MultiTrigger multi => multi.Setters,
            DataTrigger data => data.Setters,
            MultiDataTrigger multiData => multiData.Setters,
            _ => throw new InvalidOperationException("Template trigger has no setters."),
        };

    private static void RunTriggerActions(FrameworkElement scope, TriggerActionCollection actions)
    {
        foreach (var action in actions)
        {
            if (action is BeginStoryboard begin && begin.Storyboard is not null)
                WpfAnimationClock.Start(begin.Storyboard, scope, begin.Name);
            else if (action is RemoveStoryboard remove && remove.BeginStoryboardName is not null)
                WpfAnimationClock.Remove(remove.BeginStoryboardName, scope);
        }
    }

    private static void ApplyTriggerSetters(
        Control control, IReadOnlyDictionary<string, object>? names,
        SetterBaseCollection setters, StyleApplyContext context)
    {
        foreach (var setterBase in setters)
        {
            if (setterBase is not Setter setter) continue;
            FrameworkElement part = control;
            if (setter.TargetName is not null)
            {
                if (names is null || !names.TryGetValue(setter.TargetName, out var found) ||
                    found is not FrameworkElement named)
                    throw new InvalidOperationException(
                        $"Trigger TargetName '{setter.TargetName}' names no template part.");
                part = named;
            }
            var key = setter.AttachedProperty ?? setter.Property?.Name;
            if (key is not null &&
                control.TriggerDirtied?.Any(d => ReferenceEquals(d.Target, part) && d.Key == key) != true)
            {
                control.TriggerDirtied ??= new List<TriggerDirtied>();
                control.TriggerDirtied.Add(new TriggerDirtied(part, key, ReadCurrent(part, key), setter.Value is BindingBase));
            }
            setter.ApplyToTemplatePart(part, context);
        }
    }

    /// <summary>Restores pre-trigger base values (inactive template triggers
    /// revert, like WPF precedence; entries re-resolve fresh right after).</summary>
    private static void RestoreDirtied(Control control)
    {
        var dirtied = control.TriggerDirtied;
        if (dirtied is null || dirtied.Count == 0) return;
        control.TriggerDirtied = null;
        foreach (var dirty in dirtied)
        {
            if (dirty.BindingDriven)
            {
                var dp = Setter.ResolveTriggerBindingTarget(dirty.Target, dirty.Key);
                if (dp is not null) BindingOperations.ClearBinding(dirty.Target, dp);
                if (control.ActiveEntries is { } entries)
                    foreach (var entry in entries)
                        if (ReferenceEquals(entry.Element, dirty.Target) && entry.Target == dirty.Key)
                        {
                            entry.Live = null;
                            entry.MultiLive = null;
                        }
            }
            SetTargetValue(dirty.Target, dirty.Key, dirty.Previous);
        }
        // Deactivated action triggers run their exit storyboards after the
        // base values restore (still-active triggers keep their fired mark).
        if (control.TriggerActionsFired is { Count: > 0 } fired)
        {
            UIElement exitRoot = control.TemplateRoot ?? control;
            var exitScope = exitRoot as FrameworkElement ?? control;
            foreach (var trigger in fired.ToArray())
            {
                if (IsTemplateTriggerActive(control, exitScope, trigger)) continue;
                RunTriggerActions(exitScope, trigger.ExitActions);
                fired.Remove(trigger);
            }
        }
    }

    private static object? ReadCurrent(FrameworkElement element, string target)
    {
        if (target.Contains('.'))
        {
            var dot = target.IndexOf('.');
            var owner = XamlLoader.FindEngineType(target[..dot]);
            var dp = owner is not null ? DependencyProperty.FromName(owner, target[(dot + 1)..]) : null;
            if (dp is not null && element is DependencyObject dependency) return dependency.GetValue(dp);
            return element.GetAttached(target);
        }
        var property = element.GetType().GetProperty(target,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property is not null && property.CanRead) return property.GetValue(element);
        var direct = DependencyProperty.FromName(element.GetType(), target);
        if (direct is not null && element is DependencyObject directTarget) return directTarget.GetValue(direct);
        return element.GetAttached(target);
    }

    /// <summary>Pre-trigger base value for one trigger-set property.</summary>
    internal sealed class TriggerDirtied
    {
        public TriggerDirtied(FrameworkElement target, string key, object? previous, bool bindingDriven = false)
        {
            Target = target;
            Key = key;
            Previous = previous;
            BindingDriven = bindingDriven;
        }

        public FrameworkElement Target { get; }
        public string Key { get; }
        public object? Previous { get; }
        public bool BindingDriven { get; }
    }

    internal static void SetTemplateParents(UIElement root, FrameworkElement parent)
    {
        foreach (var element in Tree(root).OfType<FrameworkElement>())
            element.TemplateParent = parent;
    }

    private static List<ContentPresenter> CollectPresenters(UIElement root) =>
        Tree(root).OfType<ContentPresenter>().Where(p => p.Content is null).ToList();

    private static void DetachTree(UIElement root)
    {
        foreach (var element in Tree(root).OfType<FrameworkElement>())
        {
            element.ClearAllBindings();
            element.TemplateParent = null;
        }
    }

    private static IEnumerable<UIElement> Tree(UIElement root)
    {
        yield return root;
        if (root is not FrameworkElement host) yield break;
        foreach (var child in host.LogicalChildren)
            foreach (var nested in Tree(child))
                yield return nested;
    }
}
