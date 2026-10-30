using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopWasm.Wpf;

/// <summary>Binding direction (TwoWay pushes target edits back on UpdateSources).</summary>
public enum BindingMode
{
    OneWay = 1,
    OneTime = 2,
    /// <summary>Target-to-source push on edits (editors call UpdateSources after each change).</summary>
    TwoWay = 0,
    OneWayToSource = 3,
    Default = 4,
}

/// <summary>
/// Binding base (matches System.Windows.Data.BindingBase): fallback and
/// format knobs shared by all bindings. FallbackValue applies when the
/// path does not resolve, TargetNullValue when it resolves to null.
/// </summary>
public abstract class BindingBase
{
    /// <summary>Value pushed when the source path does not resolve.</summary>
    public object? FallbackValue { get; set; }

    /// <summary>Value pushed when the source path resolves to null.</summary>
    public object? TargetNullValue { get; set; }

    /// <summary>Composite format applied when the target property is a string.</summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Attaches a live transfer to a FrameworkElement target (matches
    /// BindingBase.ProvideValue): DependencyProperty and PropertyInfo
    /// targets bind by property name through a real BindingExpression
    /// (initial push included); targets that cannot take a transfer get
    /// the binding itself back, like WPF's deferred path. Converted
    /// ScreenToGif.Util DynamicResourceBinding calls this with an
    /// IProvideValueTarget service after building its converter binding.
    /// </summary>
    public virtual object? ProvideValue(IServiceProvider serviceProvider)
    {
        if ((serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)
            is not { TargetObject: FrameworkElement target })
            return this;
        var targetProperty = (serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)
            ?.TargetProperty;
        if (targetProperty is DependencyProperty dp && dp.IsAttached && this is Binding attachedBinding)
        {
            // Attached DP targets (converted Lang extensions binding
            // InfoElement.Placeholder, ...) ride the attached bag like XAML
            // attached bindings; the property-name path below only fits CLR
            // properties.
            var attachedName = dp.OwnerType.Name + "." + dp.Name;
            var attached = new BindingExpression(target, attachedName, attachedBinding, attached: true);
            target.AddBindingExpression(attached);
            return attached;
        }
        var propertyName = targetProperty switch
        {
            DependencyProperty plain => plain.Name,
            System.Reflection.PropertyInfo property => property.Name,
            _ => null,
        };
        if (propertyName is null || this is not Binding binding) return this;
        var expression = new BindingExpression(target, propertyName, binding);
        target.AddBindingExpression(expression);
        return expression;
    }
}

/// <summary>Relative-source lookup mode (matches System.Windows.Data.RelativeSourceMode).</summary>
public enum RelativeSourceMode
{
    PreviousData,
    TemplatedParent,
    Self,
    FindAncestor,
}

/// <summary>
/// Relative source (matches System.Windows.Data.RelativeSource): Self, the
/// template parent, or an ancestor of a given type/level. Resolved by the
/// binding engine against the live tree (converted gallery pages bind to
/// their page and to templated parents this way).
/// </summary>
public sealed class RelativeSource
{
    public RelativeSource() : this(RelativeSourceMode.FindAncestor) { }

    public RelativeSource(RelativeSourceMode mode) => Mode = mode;

    public RelativeSource(RelativeSourceMode mode, Type? ancestorType, int ancestorLevel)
    {
        Mode = mode;
        AncestorType = ancestorType;
        AncestorLevel = ancestorLevel;
    }

    public RelativeSourceMode Mode { get; set; } = RelativeSourceMode.FindAncestor;

    /// <summary>
    /// Self lookup (matches RelativeSource.Self; converted docking managers
    /// bind it for their menu-button bindings). A fresh instance per call,
    /// like WPF.
    /// </summary>
    public static RelativeSource Self => new(RelativeSourceMode.Self);

    /// <summary>Ancestor type for FindAncestor (null matches any ancestor).</summary>
    public Type? AncestorType { get; set; }

    /// <summary>Which matching ancestor to take (1-based, like WPF).</summary>
    public int AncestorLevel { get; set; } = 1;
}

/// <summary>
/// Source path (matches System.Windows.PropertyPath): the dotted CLR
/// property/field walk off the source ("" = the source itself), or a
/// dependency-property step naming that property. Converted ScreenToGif
/// code assigns <c>new PropertyPath(...)</c> (Editor bindings, TimeBox DP
/// reads, DynamicResourceBinding); the implicit conversions keep
/// engine-internal string reads compiling.
/// </summary>
public sealed class PropertyPath : IEquatable<PropertyPath>
{
    private readonly List<object> _parameters;

    /// <summary>Dotted path ("" = the source itself).</summary>
    public PropertyPath(string? path)
    {
        Path = path ?? string.Empty;
        _parameters = new List<object>();
    }

    /// <summary>Dependency-property step (matches new PropertyPath(dp)).</summary>
    public PropertyPath(DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Path = property.Name;
        _parameters = new List<object> { property };
    }

    /// <summary>
    /// Parameter step (matches new PropertyPath(object), verified against the
    /// .NET Framework 4.8 reference: a DependencyProperty parameter takes the
    /// DP step, anything else travels as the single indexed parameter).
    /// Converted docking managers bind it for their menu buttons.
    /// </summary>
    public PropertyPath(object? parameter)
    {
        if (parameter is DependencyProperty property)
        {
            Path = property.Name;
            _parameters = new List<object> { property };
        }
        else
        {
            Path = "(0)";
            _parameters = parameter is null ? new List<object>() : new List<object> { parameter };
        }
    }

    /// <summary>
    /// Indexed path (matches new PropertyPath(string, object[]), verified
    /// against the 9.0.21 and .NET Framework 4.8 references; converted shell
    /// chrome binds it). A null parameter array means no parameters.
    /// </summary>
    public PropertyPath(string? path, params object?[]? pathParameters)
    {
        Path = path ?? string.Empty;
        _parameters = pathParameters is null ? new List<object>() : new List<object>(pathParameters!);
    }

    /// <summary>Dotted path ("" = the source itself).</summary>
    public string Path { get; }

    /// <summary>Indexed parameters (the DP for the DependencyProperty step, else empty).</summary>
    public IReadOnlyList<object> PathParameters => _parameters;

    public static implicit operator string(PropertyPath? path) => path?.Path ?? string.Empty;

    public static implicit operator PropertyPath(string? path) => new(path);

    public bool Equals(PropertyPath? other) =>
        other is not null && string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PropertyPath other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

    public override string ToString() => Path;
}

/// <summary>
/// Markup/config object describing a source read: <see cref="Path"/> off
/// <see cref="Source"/> (or the target's inherited DataContext),
/// pushed one-way into a target CLR property.
/// </summary>
public sealed class Binding : BindingBase
{
    public Binding() : this(string.Empty) { }
    public Binding(string path)
    {
        Path = new PropertyPath(path);
    }

    /// <summary>Dotted property/field path off the source ("" = the source itself).</summary>
    public PropertyPath Path { get; set; } = new PropertyPath(string.Empty);

    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>Explicit source; null means the target's inherited DataContext.</summary>
    public object? Source { get; set; }

    /// <summary>Named element source (resolved through FindName when Source is null).</summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Deferred converter key from Converter={StaticResource Key} (resolved
    /// at flush/apply/inflate time, when resources are complete, like WPF's
    /// load-order handling for forward references).
    /// </summary>
    internal string? DeferredConverterKey { get; set; }

    /// <summary>
    /// Deferred source key from Source={StaticResource Key} inside templates
    /// (resolved at evaluate time against the target scope, when theme
    /// resources are complete, like the deferred converter path).
    /// </summary>
    internal string? DeferredSourceKey { get; set; }

    /// <summary>
    /// Direct source head (explicit Source, else the deferred StaticResource
    /// key resolved against the target scope); the ElementName/relative/
    /// DataContext tail stays with each resolution site, like WPF's
    /// source-precedence order.
    /// </summary>
    internal object? EffectiveDirectSource(FrameworkElement target)
    {
        if (Source is not null) return Source;
        if (DeferredSourceKey is not { } key) return null;
        if (target.TryFindResource(key, out var resource)) return resource;
        if (ThemeDictionaries.TryFindThemeValue(target, key, out resource)) return resource;
        return null;
    }

    /// <summary>Tree-relative source (Self/TemplatedParent/FindAncestor); null uses DataContext.</summary>
    public RelativeSource? RelativeSource { get; set; }

    public UpdateSourceTrigger UpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;

    /// <summary>Value converter applied on target push (ConvertBack on source push).</summary>
    public IValueConverter? Converter { get; set; }

    public object? ConverterParameter { get; set; }

    public System.Globalization.CultureInfo ConverterCulture { get; set; } =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Skips update cycles (mirrors WPF's Binding.DoNothing sentinel).</summary>
    public static readonly object DoNothing = new();

    /// <summary>
    /// Value rules checked on source push (matches Binding.ValidationRules;
    /// the gallery TextBoxPage alphabetic rule binds here). A failing rule
    /// blocks the source update and records element error state.
    /// </summary>
    public System.Collections.ObjectModel.Collection<ValidationRule> ValidationRules { get; } = new();

    /// <summary>
    /// Raise Validation.Error on error add/remove (matches
    /// Binding.NotifyOnValidationError; default false like WPF). Error state
    /// (Validation.GetHasError/GetErrors) always updates.
    /// </summary>
    public bool NotifyOnValidationError { get; set; }
}

/// <summary>Multi-value converter contract (matches System.Windows.Data.IMultiValueConverter).</summary>
public interface IMultiValueConverter
{
    object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
    object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture);
}

/// <summary>
/// Composite source read (matches System.Windows.Data.MultiBinding):
/// child bindings resolve against their own sources and combine through
/// <see cref="Converter"/> or <see cref="BindingBase.StringFormat"/>.
/// </summary>
/// <summary>Priority binding (matches System.Windows.Data.PriorityBinding):
/// an ordered <see cref="BindingBase"/> collection where the first child
/// yielding a value wins (character-counter DataTrigger conditions bind
/// MaxLength-with-fallback this way); evaluation lives with the trigger
/// and binding engines, like the composite converter path.</summary>
public sealed class PriorityBinding : BindingBase
{
    /// <summary>Ordered candidate bindings (first viable value wins).</summary>
    public System.Collections.ObjectModel.Collection<BindingBase> Bindings { get; } = new();

    /// <summary>
    /// Evaluates the child bindings in order and returns the first
    /// synchronously available value (mirrors native PriorityBinding:
    /// first viable child wins; nothing viable falls back to the
    /// composite FallbackValue). Non-Binding children are skipped the way
    /// trigger evaluation skips them.
    /// </summary>
    internal object? Evaluate(FrameworkElement target)
    {
        foreach (var candidate in Bindings)
        {
            if (candidate is not Binding child) continue;
            if (TriggerEvaluation.TryEvaluateTriggerChild(target, child, out var value)) return value;
        }
        return FallbackValue;
    }
}

public sealed class MultiBinding : BindingBase
{
    /// <summary>Child bindings (matches WPF: the collection takes
    /// <see cref="BindingBase"/>; evaluation reads the <see cref="Binding"/>
    /// members and vetoes the combination for anything else, like the
    /// converted HandyControl RectangleCircular wiring which adds plain
    /// Bindings through this very getter).</summary>
    public System.Collections.ObjectModel.Collection<BindingBase> Bindings { get; } = new();

    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    /// <summary>Multi-value converter applied to the child values.</summary>
    public IMultiValueConverter? Converter { get; set; }

    /// <summary>
    /// Deferred converter key from Converter={StaticResource Key} (resolved
    /// at apply time against the target scope, like single bindings).
    /// </summary>
    internal string? DeferredConverterKey { get; set; }

    public object? ConverterParameter { get; set; }

    public System.Globalization.CultureInfo ConverterCulture { get; set; } =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Raise Validation.Error on error add/remove (matches
    /// MultiBinding.NotifyOnValidationError; retained like the single
    /// binding knob, default false like WPF).</summary>
    public bool NotifyOnValidationError { get; set; }

    /// <summary>Honor IDataErrorInfo on child sources (matches
    /// MultiBinding.ValidatesOnDataErrors; retained, default true like
    /// WPF; multi-child validation stays a boundary).</summary>
    public bool ValidatesOnDataErrors { get; set; } = true;

    /// <summary>
    /// Evaluates the child bindings for an attached target: each child
    /// resolves its own source (explicit, relative, or the target's
    /// inherited DataContext); unresolvable children fall back per child,
    /// and a child with no value and no fallback vetoes the combination.
    /// </summary>
    internal object? Evaluate(FrameworkElement target)
    {
        var values = new object?[Bindings.Count];
        for (var i = 0; i < Bindings.Count; i++)
        {
            if (Bindings[i] is not Binding child) return null;
            var source = child.EffectiveDirectSource(target)
                ?? (child.ElementName is string name ? target.FindName(name) : null)
                ?? (child.RelativeSource is not null
                    ? BindingExpression.ResolveRelativeSource(target, child.RelativeSource)
                    : target.DataContext);
            if (source is null || !BindingExpression.TryRead(source, child.Path, out var value))
            {
                if (child.FallbackValue is not null) value = child.FallbackValue;
                else return null;
            }
            else if (value is null)
            {
                // WPF parity: the child converter still runs on a null source
                // value; TargetNullValue applies to the converted result.
                if (child.Converter is not null)
                    value = child.Converter.Convert(null, typeof(object), child.ConverterParameter, child.ConverterCulture);
                if (value is null && child.TargetNullValue is not null)
                    value = child.TargetNullValue;
            }
            else if (child.Converter is not null)
                value = child.Converter.Convert(value, typeof(object), child.ConverterParameter, child.ConverterCulture);
            values[i] = value;
        }
        var converter = Converter;
        if (converter is null && DeferredConverterKey is { } key
            && target.TryFindResource(key, out var resource) && resource is IMultiValueConverter found)
            converter = found;
        if (converter is not null)
            return converter.Convert(values, typeof(object), ConverterParameter, ConverterCulture);
        if (StringFormat is { } format)
        {
            if (format.StartsWith("{}", StringComparison.Ordinal)) format = format[2..];
            return string.Format(ConverterCulture, format, values);
        }
        return values.Length == 1 ? values[0] : string.Concat(values.Select(v => v?.ToString()));
    }
}

/// <summary>
/// Live transfer root (matches System.Windows.Data.BindingExpressionBase):
/// the <see cref="BindingBase"/> source plus its target element, with
/// UpdateTarget/UpdateSource pumps. The engine's concrete transfer is
/// <see cref="BindingExpression"/>; converted ScreenToGif code names this
/// base through BindingOperations.GetBindingExpressionBase.
/// </summary>
public abstract class BindingExpressionBase
{
    /// <summary>Transfer target and its source description.</summary>
    protected BindingExpressionBase(DependencyObject target, BindingBase binding)
    {
        Target = target;
        BindingBase = binding;
    }

    /// <summary>Object the transfer lands on (any DependencyObject since slice-80).</summary>
    public DependencyObject Target { get; }

    /// <summary>
    /// CLR/attached name the transfer lands on (null when unnamed). Single
    /// and composite transfers override this; the element registry keys
    /// replace/clear by it (covers PriorityBinding transfers as well).
    /// </summary>
    public virtual string? TargetProperty => null;

    /// <summary>Source description (the concrete <see cref="Binding"/> on engine transfers).</summary>
    public BindingBase BindingBase { get; }

    /// <summary>Read the path and push into the target (see BindingExpression).</summary>
    public abstract void UpdateTarget();

    /// <summary>Read the target CLR property and push into the source path.</summary>
    public abstract void UpdateSource();

    /// <summary>Stop pushing and unsubscribe (engine transfers leave the target list).</summary>
    public virtual void Detach() { }
}

/// <summary>
/// Live one-way link from a source path to a target CLR property.
/// Created by <see cref="FrameworkElement.SetBinding"/>; call
/// <see cref="Detach"/> to unsubscribe.
/// </summary>
public sealed class BindingExpression : BindingExpressionBase
{
    private readonly PropertyInfo? _targetProperty;
    private readonly string? _attachedName;

    internal BindingExpression(FrameworkElement target, string propertyName, Binding binding)
        : base(target, binding)
    {
        Binding = binding;
        _targetProperty = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{target.GetType().Name}' has no bindable property '{propertyName}'.");
        if (!_targetProperty.CanWrite || _targetProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException($"'{target.GetType().Name}.{propertyName}' has no public setter.");
        Rebind();
    }

    internal BindingExpression(FrameworkElement target, string attachedName, Binding binding, bool attached)
        : base(target, binding)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachedName);
        if (!attached) throw new ArgumentException("Use the property overload for CLR bindings.", nameof(attached));
        Binding = binding;
        _attachedName = attachedName;
        Rebind();
    }

    public Binding Binding { get; }

    private FrameworkElement _target => (FrameworkElement)Target;

    public override string TargetProperty => _attachedName ?? _targetProperty!.Name;

    /// <summary>Source currently subscribed (null when unresolvable).</summary>
    public object? Source { get; private set; }

    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _chain = new();
    private bool _staticHooked;
    private BindingMode EffectiveMode => Binding.Mode == BindingMode.Default
        ? _target is TextBox && TargetProperty == "Text" ? BindingMode.TwoWay
        : _target is DatePicker && (TargetProperty == "SelectedDate" || TargetProperty == "Text") ? BindingMode.TwoWay
        : _target is ListBox && (TargetProperty == "SelectedItem" || TargetProperty == "SelectedIndex") ? BindingMode.TwoWay
        : _target is Selector && (TargetProperty == "SelectedItem" || TargetProperty == "SelectedIndex") ? BindingMode.TwoWay
        : BindingMode.OneWay
        : Binding.Mode;

    /// <summary>Target value type (attached bindings store plain objects).</summary>
    private Type LandType => _targetProperty?.PropertyType ?? typeof(object);

    /// <summary>Re-resolve the source (DataContext may have changed) and push.</summary>
    internal void Rebind()
    {
        DetachSource();
        Source = Binding.EffectiveDirectSource(_target)
            ?? (Binding.ElementName is string name ? _target.FindName(name) : null)
            ?? ResolveRelativeSource()
            ?? _target.DataContext;
        if (EffectiveMode is not (BindingMode.OneTime or BindingMode.OneWayToSource))
            TrackChain();
        UpdateTarget();
        if (EffectiveMode == BindingMode.OneWayToSource) UpdateSource();
    }

    internal void DetachSource()
    {
        foreach (var (source, handler) in _chain)
            source.PropertyChanged -= handler;
        _chain.Clear();
        if (_staticHooked)
        {
            SystemParameters.StaticPropertyChanged -= OnStaticChanged;
            _staticHooked = false;
        }
        Source = null;
    }

    private object? ResolveRelativeSource() => ResolveRelativeSource(_target, Binding.RelativeSource);

    internal static object? ResolveRelativeSource(FrameworkElement target, RelativeSource? relative)
    {
        if (relative is null) return null;
        return relative.Mode switch
        {
            RelativeSourceMode.Self => target,
            RelativeSourceMode.TemplatedParent => target.TemplateParent,
            RelativeSourceMode.FindAncestor => FindAncestorElement(target, relative.AncestorType, relative.AncestorLevel),
            _ => throw new InvalidOperationException(
                $"RelativeSourceMode.{relative.Mode} has no binding source here (no items context)."),
        };
    }

    internal static FrameworkElement? FindAncestorElement(FrameworkElement target, Type? ancestorType, int level)
    {
        var skipped = 0;
        for (var node = target.Parent as FrameworkElement; node is not null; node = node.Parent as FrameworkElement)
        {
            if (ancestorType is null || ancestorType.IsInstanceOfType(node))
            {
                skipped++;
                if (skipped >= Math.Max(1, level)) return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Subscribes every notifying link of the source path (nested view-model
    /// chains like ViewModel.IsEnabled push, like WPF) plus the static
    /// metric event for parenthesized SystemParameters segments.
    /// </summary>
    private void TrackChain()
    {
        foreach (var (subscribed, handler) in _chain)
            subscribed.PropertyChanged -= handler;
        _chain.Clear();
        var current = Source;
        if (current is null) return;
        if (!_staticHooked && NeedsStaticRefresh(Binding.Path))
        {
            SystemParameters.StaticPropertyChanged += OnStaticChanged;
            _staticHooked = true;
        }
        foreach (var segment in SplitPath(Binding.Path.Path))
        {
            if (string.IsNullOrEmpty(segment)) return;
            if (IsStaticSegment(segment)) continue;
            if (current is INotifyPropertyChanged inpc)
            {
                var watched = segment;
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == watched)
                    {
                        UpdateTarget();
                        TrackChain();
                    }
                };
                inpc.PropertyChanged += handler;
                _chain.Add((inpc, handler));
            }
            if (!TryReadSegment(current, segment, out var next)) return;
            current = next;
        }
    }

    private void OnStaticChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTarget();
        TrackChain();
    }

    private static bool NeedsStaticRefresh(string path)
    {
        foreach (var segment in SplitPath(path))
            if (IsStaticSegment(segment)) return true;
        return false;
    }

    internal static bool IsStaticSegment(string segment) =>
        segment.StartsWith('(') && segment.EndsWith(')') && segment.Length > 2;

    /// <summary>
    /// Splits a binding path on top-level dots: parenthesized owner-qualified
    /// steps like (PreviewSlider.PreviewPosition) keep their inner dot (WPF
    /// parity); paths without parens split exactly like string.Split('.').
    /// </summary>
    internal static string[] SplitPath(string path)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < path.Length; i++)
        {
            var ch = path[i];
            if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            else if (ch == '.' && depth == 0)
            {
                parts.Add(path[start..i]);
                start = i + 1;
            }
        }
        parts.Add(path[start..]);
        return [.. parts];
    }

    /// <summary>Stop pushing and unsubscribe.</summary>
    public override void Detach()
    {
        DetachSource();
        _target.RemoveBinding(this);
    }

    /// <summary>
    /// Read the target CLR property and push it into the source path
    /// (TwoWay only; skipped when the source already holds an equal value,
    /// which also breaks target/source update cycles).
    /// </summary>
    public override void UpdateSource() => PushSource(force: true);

    /// <summary>Auto-push used by editors (skips Explicit/LostFocus triggers).</summary>
    internal void PushSource(bool force)
    {
        if (EffectiveMode is not (BindingMode.TwoWay or BindingMode.OneWayToSource)) return;
        if (!force && Binding.UpdateSourceTrigger is UpdateSourceTrigger.Explicit or UpdateSourceTrigger.LostFocus) return;
        PushSourceCore();
    }

    /// <summary>
    /// Pushes a caller-supplied target value (the legacy ListBox pushes its
    /// selected data item instead of the SelectedItem container), honoring
    /// the binding mode and update trigger like an editor push.
    /// </summary>
    internal void PushSourceValue(object? rawValue)
    {
        if (EffectiveMode is not (BindingMode.TwoWay or BindingMode.OneWayToSource)) return;
        if (Binding.UpdateSourceTrigger is UpdateSourceTrigger.Explicit or UpdateSourceTrigger.LostFocus) return;
        PushCore(rawValue);
    }

    /// <summary>
    /// Validated source push (WPF transfer order): RawProposedValue rules run
    /// on the raw target value and block the update when invalid, then
    /// ConvertedProposedValue rules run after conversion, then UpdatedValue/
    /// CommittedValue rules run after the source write (they report without
    /// rolling the write back). Rule failures replace this binding's errors.
    /// </summary>
    private void PushSourceCore()
    {
        var value = _attachedName is not null ? _target.GetAttached(_attachedName) : _targetProperty!.GetValue(_target);
        PushCore(value);
    }

    private void PushCore(object? value)
    {
        var source = Binding.EffectiveDirectSource(_target)
            ?? (Binding.ElementName is string name ? _target.FindName(name) : null)
            ?? ResolveRelativeSource()
            ?? _target.DataContext;
        if (source is null || string.IsNullOrEmpty(Binding.Path)) return;
        var rawErrors = RunRules(ValidationStep.RawProposedValue, value, targetUpdatedOnly: false);
        if (rawErrors.Count > 0) { ApplyValidation(rawErrors); return; }
        var converted = ConvertBack(value);
        if (ReferenceEquals(converted, Binding.DoNothing)) return;
        var convertedErrors = RunRules(ValidationStep.ConvertedProposedValue, converted, targetUpdatedOnly: false);
        if (convertedErrors.Count > 0) { ApplyValidation(convertedErrors); return; }
        if (TryRead(source, Binding.Path, out var current) && Equals(current, converted))
        {
            ApplyValidation(RunPostUpdateRules(converted));
            return;
        }
        TrySetPath(source, Binding.Path, converted);
        ApplyValidation(RunPostUpdateRules(converted));
    }

    private IReadOnlyList<ValidationError> RunPostUpdateRules(object? converted)
    {
        var post = new List<ValidationError>();
        post.AddRange(RunRules(ValidationStep.UpdatedValue, converted, targetUpdatedOnly: false));
        post.AddRange(RunRules(ValidationStep.CommittedValue, converted, targetUpdatedOnly: false));
        return post;
    }

    /// <summary>Runs the binding's rules for one transfer stage (empty when no rules).</summary>
    private IReadOnlyList<ValidationError> RunRules(ValidationStep step, object? value, bool targetUpdatedOnly)
    {
        if (Binding.ValidationRules.Count == 0) return [];
        var failures = new List<ValidationError>();
        foreach (var rule in Binding.ValidationRules)
        {
            if (rule.ValidationStep != step) continue;
            if (targetUpdatedOnly && !rule.ValidatesOnTargetUpdated) continue;
            ValidationResult result;
            try
            {
                result = rule.Validate(value, Binding.ConverterCulture);
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationError(rule, this, ex.Message, ex));
                continue;
            }
            if (!result.IsValid)
                failures.Add(new ValidationError(rule, this, result.ErrorContent, null));
        }
        return failures;
    }

    private void ApplyValidation(IReadOnlyList<ValidationError> errors)
    {
        if (Binding.ValidationRules.Count == 0) return;
        Validation.SetBindingErrors(_target, Binding, errors, Binding.NotifyOnValidationError);
    }

    /// <summary>
    /// Read the path and push into the target (FallbackValue on unresolvable
    /// paths bypasses the converter; a null source value still runs the
    /// converter like WPF, and TargetNullValue applies to the converted
    /// result when it is null). Landed values run ValidatesOnTargetUpdated
    /// rules.
    /// </summary>
    public override void UpdateTarget()
    {
        if (EffectiveMode == BindingMode.OneWayToSource) return;
        if (!TryRead(Source, Binding.Path, out var value))
        {
            if (PushFallback(Binding.FallbackValue, out var landed)) ValidateTargetUpdated(landed);
            return;
        }
        if (value is null && Binding.Converter is null)
        {
            if (PushFallback(Binding.TargetNullValue, out var landedNull)) ValidateTargetUpdated(landedNull);
            return;
        }
        var converted = Convert(value);
        if (converted is null)
        {
            if (PushFallback(Binding.TargetNullValue, out var landedConvertedNull)) ValidateTargetUpdated(landedConvertedNull);
            return;
        }
        if (ReferenceEquals(converted, Binding.DoNothing)) return;
        object? landedValue;
        try
        {
            landedValue = ConvertValue(Format(converted), LandType);
            if (_attachedName is not null) _target.SetAttached(_attachedName, landedValue);
            else _targetProperty!.SetValue(_target, landedValue);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            // WPF keeps the old value and traces; v1 keeps the old value.
            return;
        }
        ValidateTargetUpdated(landedValue);
    }

    /// <summary>Runs ValidatesOnTargetUpdated rules on a freshly landed target value.</summary>
    private void ValidateTargetUpdated(object? landed)
    {
        if (Binding.ValidationRules.Count == 0) return;
        if (!Binding.ValidationRules.Any(r => r.ValidatesOnTargetUpdated)) return;
        ApplyValidation(RunRules(ValidationStep.RawProposedValue, landed, targetUpdatedOnly: true));
    }

    /// <summary>Pushes a fallback value through plain conversion (no converter); true when set.</summary>
    private bool PushFallback(object? fallback, out object? landed)
    {
        landed = null;
        if (fallback is null) return false;
        try
        {
            landed = ConvertValue(fallback, LandType);
            if (_attachedName is not null) _target.SetAttached(_attachedName, landed);
            else _targetProperty!.SetValue(_target, landed);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>Applies StringFormat for string targets (attached names always format, like WPF).</summary>
    private object? Format(object? value)
    {
        if (Binding.StringFormat is null || value is null) return value;
        if (_attachedName is null && _targetProperty!.PropertyType != typeof(string)) return value;
        try
        {
            return string.Format(Binding.ConverterCulture, Binding.StringFormat, value);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private object? Convert(object? value)
    {
        if (Binding.Converter is null) return value;
        try
        {
            return Binding.Converter.Convert(value, LandType, Binding.ConverterParameter, Binding.ConverterCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or NotSupportedException or ArgumentException)
        {
            return Binding.DoNothing;
        }
    }

    private object? ConvertBack(object? value)
    {
        if (Binding.Converter is null) return value;
        try
        {
            return Binding.Converter.ConvertBack(value, typeof(object), Binding.ConverterParameter, Binding.ConverterCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or NotSupportedException or ArgumentException)
        {
            return Binding.DoNothing;
        }
    }

    internal static bool TryRead(object? source, string path, out object? value)
    {
        value = null;
        if (source is null) return false;
        if (string.IsNullOrEmpty(path)) { value = source; return true; }
        // Self path (SmartHint RenderTransform composites read the
        // templated parent itself): Path="." binds the source, like WPF.
        if (path == ".") { value = source; return true; }
        var current = source;
        var segments = SplitPath(path);
        for (var i = 0; i < segments.Length; i++)
        {
            if (!TryReadSegment(current, segments[i], out var next))
            {
                // Owner-qualified steps (RatingBar.Orientation on a
                // RatingBar): a missed step that names a type qualifies
                // the next step, like WPF TypeName.Property path form.
                if (i + 1 >= segments.Length || !IsPathQualifier(segments[i])) return false;
                next = current;
            }
            current = next;
        }
        value = current;
        return true;
    }

    /// <summary>Memoized owner-type hits for binding-path qualifier
    /// steps (misses stay uncached: staged images load lazily, so a
    /// miss now can resolve after a later image loads).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> PathQualifierTypes = new(StringComparer.Ordinal);

    /// <summary>True when a path step names a type (engine or
    /// converted) rather than a member, qualifying the next step like
    /// WPF TypeName.Property path form.</summary>
    internal static bool IsPathQualifier(string segment)
    {
        if (string.IsNullOrEmpty(segment) || IsStaticSegment(segment)) return false;
        if (PathQualifierTypes.ContainsKey(segment)) return true;
        if (XamlLoader.FindEngineType(segment) is not null
            || XamlLoader.FindConvertedShortNameMatches(segment).Count > 0)
        {
            PathQualifierTypes[segment] = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads one path segment: a CLR property/field, or a parenthesized
    /// static/attached reference like (SystemParameters.HighContrast) and
    /// (AutomationProperties.Name).
    /// </summary>
    internal static bool TryReadSegment(object? source, string segment, out object? value)
    {
        value = null;
        if (source is null || string.IsNullOrEmpty(segment)) return false;
        if (IsStaticSegment(segment)) return TryReadStaticSegment(source, segment[1..^1], out value);
        var member = (MemberInfo?)source.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance)
            ?? source.GetType().GetField(segment, BindingFlags.Public | BindingFlags.Instance);
        if (member is PropertyInfo p)
        {
            if (!p.CanRead) return false;
            value = p.GetValue(source);
            return true;
        }
        if (member is FieldInfo f)
        {
            value = f.GetValue(source);
            return true;
        }
        return false;
    }

    private static bool TryReadStaticSegment(object? source, string inner, out object? value)
    {
        value = null;
        var dot = inner.LastIndexOf('.');
        if (dot <= 0) return false;
        var ownerName = inner[..dot];
        var memberName = inner[(dot + 1)..];
        var shortOwner = ownerName.Contains(':') ? ownerName[(ownerName.IndexOf(':') + 1)..] : ownerName;
        var owner = XamlLoader.FindEngineType(shortOwner);
        var property = owner?.GetProperty(memberName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (property is not null && property.CanRead)
        {
            value = property.GetValue(null);
            return true;
        }
        // Converted attached-DP read ((hc:PreviewSlider.PreviewPosition) on a
        // staged view): the converted field carries the DP identity, and
        // GetValue honors its default plus Inherits, like WPF. Unknown or
        // ambiguous owners keep the legacy attached-bag behavior below.
        if (source is DependencyObject target &&
            XamlLoader.TryResolveConvertedAttachedDp(shortOwner, memberName) is { } convertedDp)
        {
            value = target.GetValue(convertedDp);
            return true;
        }
        // Attached read against the current source object
        // ((AutomationProperties.Name) on a templated parent).
        if (source is FrameworkElement element)
        {
            value = element.GetAttached(inner);
            return true;
        }
        return false;
    }

    /// <summary>Walks to the leaf's owner and sets it (false when any step fails).</summary>
    internal static bool TrySetPath(object source, string path, object? value)
    {
        var dot = path.LastIndexOf('.');
        var owner = dot < 0 ? source : TryRead(source, path[..dot], out var o) ? o : null;
        if (owner is null) return false;
        var leaf = dot < 0 ? path : path[(dot + 1)..];
        var type = owner.GetType();
        if (type.GetProperty(leaf, BindingFlags.Public | BindingFlags.Instance) is { } p)
        {
            if (!p.CanWrite || p.SetMethod?.IsPublic != true) return false;
            try { p.SetValue(owner, value is null ? null : ConvertValue(value, p.PropertyType)); }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
            return true;
        }
        if (type.GetField(leaf, BindingFlags.Public | BindingFlags.Instance) is { } f)
        {
            try { f.SetValue(owner, value is null ? null : ConvertValue(value, f.FieldType)); }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
            return true;
        }
        return false;
    }

    internal static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null) return null;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type.IsInstanceOfType(value)) return value;
        if (type == typeof(string)) return value.ToString();
        if (type.IsEnum && value is string name) return Enum.Parse(type, name);
        if (type == typeof(Color) && value is string colorText)
            return StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(colorText));
        // WPF ThicknessConverter parity: CLR resource attributes carry
        // "l,t,r,b" text (MaterialDesign PackIcon Margin="4,0,4,0"), which
        // ChangeType cannot convert.
        if (type == typeof(Thickness) && value is string thicknessText)
            return Thickness.Parse(thicknessText);
        // WPF ImageSourceConverter parity: gallery card templates bind
        // Image.Source to a pack Uri property (ControlInfoDataItem.ImageSource).
        // Without this the landing throws, the old null stays, and the icon
        // slot never emits a DrawImage. Decode stays lazy inside BitmapImage
        // (null pixels when the pack bytes are absent, like literal sources).
        if (typeof(ImageSource).IsAssignableFrom(type))
        {
            if (value is Uri uri) return new BitmapImage(uri);
            if (value is string text) return new BitmapImage(new Uri(text, UriKind.RelativeOrAbsolute));
        }
        // Comma-separated arrays (vendor converters take enum arrays like
        // "Split, Left" for VisiblePlacements): each token converts through
        // this same path, like XAML element-type conversion.
        if (type.IsArray && type.GetArrayRank() == 1 && value is string list)
        {
            var elementType = type.GetElementType()!;
            var tokens = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var array = Array.CreateInstance(elementType, tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
                array.SetValue(ConvertValue(tokens[i], elementType), i);
            return array;
        }
        return System.Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// DP-store-backed transfer for non-<see cref="FrameworkElement"/> targets
/// (matches WPF binding any DependencyObject DP). Created by
/// <see cref="BindingOperations.SetBinding"/> when the target carries no
/// CLR-property transfer surface; UpdateTarget resolves Binding.Source
/// through the path with the WPF transfer order (converter on non-null,
/// TargetNullValue on converted null, FallbackValue on unresolvable paths,
/// DoNothing keeps the old value, StringFormat applies) and lands through
/// the DP store so GetValue readers see it — the converted HandyControl
/// BindingHelper.GetString shape binds TextSearch.TextProperty on a fresh
/// DependencyObject exactly this way. Source changes re-push through the
/// INotifyPropertyChanged chain; ElementName/RelativeSource namescopes stay
/// a boundary (plain targets carry none; RelativeSource.Self resolves).
/// </summary>
public sealed class DependencyObjectBinding : BindingExpressionBase
{
    private readonly DependencyProperty _dp;
    private readonly Binding _binding;
    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _chain = new();

    internal DependencyObjectBinding(DependencyObject target, DependencyProperty dp, Binding binding)
        : base(target, binding)
    {
        _dp = dp;
        _binding = binding;
        TrackChain();
        UpdateTarget();
    }

    internal DependencyProperty Dp => _dp;

    private object? Source => (Target is FrameworkElement fe ? _binding.EffectiveDirectSource(fe) : _binding.Source)
        ?? (_binding.RelativeSource?.Mode == RelativeSourceMode.Self ? Target : null);

    private BindingMode EffectiveMode =>
        _binding.Mode == BindingMode.Default ? BindingMode.OneWay : _binding.Mode;

    /// <summary>Read the path and push into the DP store (see class notes).</summary>
    public override void UpdateTarget()
    {
        if (EffectiveMode == BindingMode.OneWayToSource) return;
        if (!BindingExpression.TryRead(Source, _binding.Path, out var value))
        {
            PushFallback(_binding.FallbackValue);
            return;
        }
        if (value is null && _binding.Converter is null)
        {
            PushFallback(_binding.TargetNullValue);
            return;
        }
        var converted = Convert(value);
        if (converted is null)
        {
            PushFallback(_binding.TargetNullValue);
            return;
        }
        if (ReferenceEquals(converted, Binding.DoNothing)) return;
        object? landed;
        try
        {
            landed = BindingExpression.ConvertValue(Format(converted), _dp.PropertyType);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            // WPF keeps the old value and traces; v1 keeps the old value.
            return;
        }
        try
        {
            Target.SetValue(_dp, landed);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;
        }
        ValidateTargetUpdated(landed);
    }

    /// <summary>Read the DP store and push into the source path (TwoWay only).</summary>
    public override void UpdateSource()
    {
        if (EffectiveMode is not (BindingMode.TwoWay or BindingMode.OneWayToSource)) return;
        if (_binding.UpdateSourceTrigger is UpdateSourceTrigger.Explicit or UpdateSourceTrigger.LostFocus) return;
        PushCore(Target.GetValue(_dp));
    }

    internal void PushCore(object? value)
    {
        var source = Source;
        if (source is null || string.IsNullOrEmpty(_binding.Path)) return;
        var rawErrors = RunRules(ValidationStep.RawProposedValue, value);
        if (rawErrors.Count > 0) { ApplyValidation(rawErrors); return; }
        var converted = ConvertBack(value);
        if (ReferenceEquals(converted, Binding.DoNothing)) return;
        var convertedErrors = RunRules(ValidationStep.ConvertedProposedValue, converted);
        if (convertedErrors.Count > 0) { ApplyValidation(convertedErrors); return; }
        if (BindingExpression.TryRead(source, _binding.Path, out var current) && Equals(current, converted))
        {
            ApplyValidation(RunPostUpdateRules(converted));
            return;
        }
        BindingExpression.TrySetPath(source, _binding.Path, converted);
        ApplyValidation(RunPostUpdateRules(converted));
    }

    /// <summary>Stop pushing and unsubscribe (ClearBinding also clears the DP value).</summary>
    public override void Detach()
    {
        foreach (var (subscribed, handler) in _chain)
            subscribed.PropertyChanged -= handler;
        _chain.Clear();
    }

    private void TrackChain()
    {
        Detach();
        if (EffectiveMode is BindingMode.OneTime or BindingMode.OneWayToSource) return;
        var current = Source;
        if (current is null) return;
        foreach (var segment in BindingExpression.SplitPath(_binding.Path.Path))
        {
            if (string.IsNullOrEmpty(segment)) return;
            if (BindingExpression.IsStaticSegment(segment)) continue;
            if (current is INotifyPropertyChanged inpc)
            {
                var watched = segment;
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == watched)
                    {
                        UpdateTarget();
                        TrackChain();
                    }
                };
                inpc.PropertyChanged += handler;
                _chain.Add((inpc, handler));
            }
            if (!BindingExpression.TryReadSegment(current, segment, out var next)) return;
            current = next;
        }
    }

    private void PushFallback(object? fallback)
    {
        if (fallback is null) return;
        object? landed;
        try
        {
            landed = BindingExpression.ConvertValue(fallback, _dp.PropertyType);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            return;
        }
        try
        {
            Target.SetValue(_dp, landed);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;
        }
        ValidateTargetUpdated(landed);
    }

    private object? Format(object? value)
    {
        if (_binding.StringFormat is null || value is null) return value;
        try
        {
            return string.Format(_binding.ConverterCulture, _binding.StringFormat, value);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private object? Convert(object? value)
    {
        if (_binding.Converter is null) return value;
        try
        {
            return _binding.Converter.Convert(value, _dp.PropertyType, _binding.ConverterParameter, _binding.ConverterCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or NotSupportedException or ArgumentException)
        {
            return Binding.DoNothing;
        }
    }

    private object? ConvertBack(object? value)
    {
        if (_binding.Converter is null) return value;
        try
        {
            return _binding.Converter.ConvertBack(value, typeof(object), _binding.ConverterParameter, _binding.ConverterCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or NotSupportedException or ArgumentException)
        {
            return Binding.DoNothing;
        }
    }

    private void ValidateTargetUpdated(object? landed)
    {
        if (_binding.ValidationRules.Count == 0) return;
        if (!_binding.ValidationRules.Any(r => r.ValidatesOnTargetUpdated)) return;
        ApplyValidation(RunRules(ValidationStep.RawProposedValue, landed));
    }

    private IReadOnlyList<ValidationError> RunRules(ValidationStep step, object? value)
    {
        if (_binding.ValidationRules.Count == 0) return [];
        var failures = new List<ValidationError>();
        foreach (var rule in _binding.ValidationRules)
        {
            if (rule.ValidationStep != step) continue;
            ValidationResult result;
            try
            {
                result = rule.Validate(value, _binding.ConverterCulture);
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationError(rule, this, ex.Message, ex));
                continue;
            }
            if (!result.IsValid)
                failures.Add(new ValidationError(rule, this, result.ErrorContent, null));
        }
        return failures;
    }

    private IReadOnlyList<ValidationError> RunPostUpdateRules(object? converted)
    {
        var post = new List<ValidationError>();
        post.AddRange(RunRules(ValidationStep.UpdatedValue, converted));
        post.AddRange(RunRules(ValidationStep.CommittedValue, converted));
        return post;
    }

    private void ApplyValidation(IReadOnlyList<ValidationError> errors)
    {
        if (_binding.ValidationRules.Count == 0) return;
        Validation.SetBindingErrors(Target, _binding, errors, _binding.NotifyOnValidationError);
    }
}

/// <summary>
/// Live composite transfer (matches the MultiBinding side of
/// FrameworkElement.SetBinding/BindingOperations): the
/// <see cref="MultiBinding"/> evaluates against its child sources and
/// pushes the combined value into the DP-named CLR property, like the
/// converted HandyControl RectangleAttach.OnCircularChanged wiring
/// (ActualWidth/ActualHeight through RectangleCircularConverter into
/// RadiusX/RadiusY). Evaluation runs at attach plus on every
/// <see cref="FrameworkElement.SizeChanged"/> (the layout-driven source
/// change; OneTime transfers evaluate once); a null or UnsetValue
/// combination leaves the old value, like WPF. Detach unsubscribes.
/// </summary>
public sealed class MultiBindingExpression : BindingExpressionBase
{
    private readonly PropertyInfo? _targetProperty;
    private readonly string? _attachedName;
    private readonly MultiBinding _multi;
    private bool _hooked;

    internal MultiBindingExpression(FrameworkElement target, string propertyName, MultiBinding multi)
        : base(target, multi)
    {
        _multi = multi;
        _targetProperty = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{target.GetType().Name}' has no bindable property '{propertyName}'.");
        if (!_targetProperty.CanWrite || _targetProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException($"'{target.GetType().Name}.{propertyName}' has no public setter.");
        if (multi.Mode != BindingMode.OneTime)
        {
            target.SizeChanged += OnSizeChanged;
            _hooked = true;
        }
        target.AddMultiBindingExpression(this);
        UpdateTarget();
    }

    internal MultiBindingExpression(FrameworkElement target, string attachedName, MultiBinding multi, bool attached)
        : base(target, multi)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachedName);
        if (!attached) throw new ArgumentException("Use the property overload for CLR bindings.", nameof(attached));
        _multi = multi;
        _attachedName = attachedName;
        if (multi.Mode != BindingMode.OneTime)
        {
            target.SizeChanged += OnSizeChanged;
            _hooked = true;
        }
        target.AddMultiBindingExpression(this);
        UpdateTarget();
    }

    /// <summary>Composite description behind this transfer.</summary>
    public MultiBinding MultiBinding => _multi;

    /// <summary>DP-named CLR property this transfer lands on.</summary>
    public override string TargetProperty => _attachedName ?? _targetProperty!.Name;

    private FrameworkElement _target => (FrameworkElement)Target;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTarget();

    /// <summary>Evaluate the composite and push into the target (null and
    /// UnsetValue combinations keep the old value, like WPF).</summary>
    public override void UpdateTarget()
    {
        var combined = _multi.Evaluate(_target);
        if (combined is null || ReferenceEquals(combined, DependencyProperty.UnsetValue)) return;
        if (_attachedName is not null)
        {
            // Attached composites land raw in the attached bag (single
            // attached bindings land plain objects the same way).
            _target.SetAttached(_attachedName, combined);
            return;
        }
        object? landed;
        try
        {
            landed = BindingExpression.ConvertValue(combined, _targetProperty!.PropertyType);
            _targetProperty.SetValue(_target, landed);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            // WPF keeps the old value and traces; v1 keeps the old value.
            return;
        }
    }

    /// <summary>Target edits never flow back through a composite (OneWay).</summary>
    public override void UpdateSource() { }

    /// <summary>Stop pushing and unsubscribe.</summary>
    public override void Detach()
    {
        if (_hooked)
        {
            _target.SizeChanged -= OnSizeChanged;
            _hooked = false;
        }
        _target.RemoveMultiBinding(this);
    }
}

/// <summary>
/// Ordered fallback among child transfers (matches
/// System.Windows.Data.PriorityBindingExpression): evaluates children in
/// order and lands the first synchronous value, mirroring native
/// first-available semantics; async/slow children fall through to the next
/// child (the engine has no async child transfers, so the first usable
/// value wins). Converted MaterialDesign SetBinding call sites name this
/// transfer through BindingOperations.SetBinding.
/// </summary>
public sealed class PriorityBindingExpression : BindingExpressionBase
{
    private readonly WeakReference<FrameworkElement> _target;
    private readonly PropertyInfo? _targetProperty;
    private readonly string? _attachedName;
    private bool _loadHooked;
    private bool _sizeHooked;

    internal PriorityBindingExpression(FrameworkElement target, string propertyName, PriorityBinding priority)
        : base(target, priority)
    {
        _target = new WeakReference<FrameworkElement>(target);
        _targetProperty = target.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{target.GetType().Name}' has no bindable property '{propertyName}'.");
        if (!_targetProperty.CanWrite || _targetProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException($"'{target.GetType().Name}.{propertyName}' has no public setter.");
        // Registry enrollment (replace/clear by name) plus the initial
        // push: sync children evaluate against the current DataContext; a
        // template parent set later re-pushes via the load hook, and
        // layout-driven sources re-push on SizeChanged, matching the
        // MultiBinding transfer.
        target.AddMultiBindingExpression(this);
        PushFirstAvailable();
        EnsureHooks(target);
    }

    internal PriorityBindingExpression(FrameworkElement target, string attachedName, PriorityBinding priority, bool attached)
        : base(target, priority)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachedName);
        if (!attached) throw new ArgumentException("Use the property overload for CLR bindings.", nameof(attached));
        _target = new WeakReference<FrameworkElement>(target);
        _attachedName = attachedName;
        target.AddMultiBindingExpression(this);
        PushFirstAvailable();
        EnsureHooks(target);
    }

    public PriorityBinding Priority => (PriorityBinding)BindingBase;

    /// <summary>BindingBase TargetProperty (the CLR or attached name this transfer lands on).</summary>
    public override string TargetProperty => _attachedName ?? _targetProperty!.Name;

    /// <summary>Re-evaluate children in order and land the first usable value.</summary>
    public override void UpdateTarget() => PushFirstAvailable();

    /// <summary>Target edits never flow back through a priority composite (OneWay).</summary>
    public override void UpdateSource() { }

    /// <summary>Stop pushing and unsubscribe.</summary>
    public override void Detach()
    {
        if (_target.TryGetTarget(out var target))
        {
            if (_loadHooked) target.Loaded -= OnTargetLoaded;
            if (_sizeHooked) target.SizeChanged -= OnTargetSizeChanged;
        }
        _loadHooked = false;
        _sizeHooked = false;
        if (_target.TryGetTarget(out var alive))
            alive.RemoveMultiBinding(this);
    }

    private void PushFirstAvailable()
    {
        if (!_target.TryGetTarget(out var target)) return;
        var value = Priority.Evaluate(target);
        if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue)) return;
        try
        {
            if (_attachedName is not null)
            {
                // Attached composites land raw in the attached bag (single
                // attached bindings land plain objects the same way).
                target.SetAttached(_attachedName, value);
                return;
            }
            var landed = BindingExpression.ConvertValue(value, _targetProperty!.PropertyType);
            _targetProperty.SetValue(target, landed);
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            // WPF keeps the old value and traces; v1 keeps the old value.
            return;
        }
    }

    private void EnsureHooks(FrameworkElement target)
    {
        if (!_loadHooked)
        {
            target.Loaded += OnTargetLoaded;
            _loadHooked = true;
        }
        if (!_sizeHooked)
        {
            target.SizeChanged += OnTargetSizeChanged;
            _sizeHooked = true;
        }
    }

    private void OnTargetLoaded(object? sender, RoutedEventArgs e) => PushFirstAvailable();

    private void OnTargetSizeChanged(object sender, SizeChangedEventArgs e) => PushFirstAvailable();
}

/// <summary>
/// Dependency-property binding entry points (matches
/// System.Windows.Data.BindingOperations): SetBinding/ClearBinding/
/// ClearAllBindings plus Binding/GetBindingExpression(Base) lookups over
/// the engine's CLR-property transfers, plus DP-store-backed transfers on
/// plain DependencyObject targets (slice-80). The DP names the CLR property
/// (ExportPanel.FrameCountProperty -&gt; "FrameCount"), matching the
/// converted ScreenToGif call sites (Editor SetBinding, TimeBox
/// GetBindingExpression, VisualHelper DataContext checks).
/// </summary>
public static class BindingOperations
{
    /// <summary>
    /// Links the DP-named CLR property to a binding (replaces any transfer
    /// on that property, like WPF). <see cref="Binding"/> transfers run
    /// through the single-source expression; <see cref="MultiBinding"/>
    /// transfers on a FrameworkElement run through the composite transfer
    /// (other composite targets stay a precise boundary).
    /// </summary>
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, DependencyObjectBinding>> ObjectBindings = new();

    public static BindingExpressionBase? SetBinding(DependencyObject target, DependencyProperty dp, BindingBase bindingBase)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(bindingBase);
        if (bindingBase is MultiBinding multi)
        {
            if (target is FrameworkElement multiElement)
            {
                if (dp.IsAttached)
                    throw new NotSupportedException(
                        "'MultiBinding' has no attached-DP transfer here (composite transfers take a CLR property).");
                multiElement.ClearBindingsFor(dp.Name);
                return new MultiBindingExpression(multiElement, dp.Name, multi);
            }
            throw new NotSupportedException(
                "'MultiBinding' has no DP transfer here (composite transfers take a FrameworkElement).");
        }
        if (bindingBase is PriorityBinding priority)
        {
            if (target is FrameworkElement priorityElement)
            {
                if (dp.IsAttached)
                    throw new NotSupportedException(
                        "'PriorityBinding' has no attached-DP transfer here (composite transfers take a CLR property).");
                priorityElement.ClearBindingsFor(dp.Name);
                return new PriorityBindingExpression(priorityElement, dp.Name, priority);
            }
            throw new NotSupportedException(
                "'PriorityBinding' has no DP transfer here (composite transfers take a FrameworkElement).");
        }
        if (bindingBase is not Binding binding)
            throw new NotSupportedException(
                $"'{bindingBase.GetType().Name}' has no DP transfer here (only Binding binds a dependency property).");
        if (target is FrameworkElement element)
        {
            if (dp.IsAttached)
            {
                // Attached DP targets (Grid.Row, converted InfoElement
                // Placeholder, ...) transfer through the attached bag keyed
                // by dotted name, like XAML attached bindings; the CLR
                // property path below has no such property and stays for
                // plain DPs.
                var attachedName = dp.OwnerType.Name + "." + dp.Name;
                element.ClearBindingsFor(attachedName);
                return element.SetAttachedBinding(attachedName, binding);
            }
            element.ClearBindingsFor(dp.Name);
            return element.SetBinding(dp.Name, binding);
        }
        // General path (slice-80): any DependencyObject DP binds through a
        // DP-store-backed transfer, e.g. the converted HandyControl
        // BindingHelper.GetString shape on TextSearch.TextProperty.
        var table = ObjectBindings.GetOrCreateValue(target);
        lock (table)
        {
            if (table.TryGetValue(dp, out var existing))
                existing.Detach();
            var transfer = new DependencyObjectBinding(target, dp, binding);
            table[dp] = transfer;
            return transfer;
        }
    }

    /// <summary>Removes the transfer on the DP-named property (no-op when absent).</summary>
    public static void ClearBinding(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        if (target is FrameworkElement element)
        {
            element.ClearBindingsFor(dp.Name);
            return;
        }
        if (!ObjectBindings.TryGetValue(target, out var table)) return;
        lock (table)
        {
            if (!table.Remove(dp, out var transfer)) return;
            transfer.Detach();
        }
        target.ClearValue(dp);
    }

    /// <summary>Removes every transfer on the target (no-op without any).</summary>
    public static void ClearAllBindings(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is FrameworkElement element)
        {
            element.ClearAllBindings();
            return;
        }
        if (!ObjectBindings.TryGetValue(target, out var table)) return;
        List<DependencyObjectBinding> transfers;
        lock (table)
        {
            transfers = [.. table.Values];
            table.Clear();
        }
        foreach (var transfer in transfers)
        {
            transfer.Detach();
            target.ClearValue(transfer.Dp);
        }
    }

    /// <summary>Transfer on the DP-named property (null when unbound).</summary>
    public static BindingExpressionBase? GetBindingExpressionBase(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        if (target is FrameworkElement element)
            return element.GetBindingExpression(dp);
        if (!ObjectBindings.TryGetValue(target, out var table)) return null;
        lock (table)
        {
            return table.TryGetValue(dp, out var transfer) ? transfer : null;
        }
    }

    /// <summary>Transfer on the DP-named property (null when unbound).</summary>
    public static BindingExpression? GetBindingExpression(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) as BindingExpression;

    /// <summary>Binding description behind the DP-named property (null when unbound).</summary>
    public static BindingBase? GetBindingBase(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp)?.BindingBase;

    /// <summary>Binding behind the DP-named property (null when unbound or composite).</summary>
    public static Binding? GetBinding(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp)?.BindingBase as Binding;

    /// <summary>Whether the DP-named property currently carries a transfer.</summary>
    public static bool IsDataBound(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) is not null;
}
