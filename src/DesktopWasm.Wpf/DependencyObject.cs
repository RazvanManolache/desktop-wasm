using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DesktopWasm.Wpf.Tests")]

namespace DesktopWasm.Wpf;

/// <summary>Change callback (matches System.Windows.PropertyChangedCallback).</summary>
public delegate void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e);

/// <summary>Change handler (matches System.Windows.DependencyPropertyChangedEventHandler).</summary>
public delegate void DependencyPropertyChangedEventHandler(object sender, DependencyPropertyChangedEventArgs e);

/// <summary>Value coercion (matches System.Windows.CoerceValueCallback).</summary>
public delegate object CoerceValueCallback(DependencyObject d, object baseValue);

/// <summary>Value validation (matches System.Windows.ValidateValueCallback).</summary>
public delegate bool ValidateValueCallback(object value);

/// <summary>Change payload (matches System.Windows.DependencyPropertyChangedEventArgs).</summary>
public readonly struct DependencyPropertyChangedEventArgs
{
    public DependencyPropertyChangedEventArgs(DependencyProperty property, object? oldValue, object? newValue)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public DependencyProperty Property { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }
}

/// <summary>
/// Value provenance (matches System.Windows.BaseValueSource; numerics
/// verified live against the 9.0.21 WindowsDesktop ref: Unknown=0,
/// Default=1, Inherited=2, DefaultStyle=3, DefaultStyleTrigger=4, Style=5,
/// TemplateTrigger=6, StyleTrigger=7, ImplicitStyleReference=8,
/// ParentTemplate=9, ParentTemplateTrigger=10, Local=11).
/// </summary>
public enum BaseValueSource
{
    Unknown = 0,
    Default = 1,
    Inherited = 2,
    DefaultStyle = 3,
    DefaultStyleTrigger = 4,
    Style = 5,
    TemplateTrigger = 6,
    StyleTrigger = 7,
    ImplicitStyleReference = 8,
    ParentTemplate = 9,
    ParentTemplateTrigger = 10,
    Local = 11,
}

/// <summary>
/// Value-source snapshot (matches System.Windows.ValueSource): the base
/// provenance plus expression/animation/coercion/current flags, verified
/// live: local sets report Local with all flags false; styled values report
/// Style; bound values report Local with IsExpression; coerced writes report
/// IsCoerced only when coercion changed the value; SetCurrentValue reports
/// Local with IsCoerced and IsCurrent; a registered-but-unticked animation
/// still reports IsAnimated false.
/// </summary>
public readonly struct ValueSource
{
    internal ValueSource(BaseValueSource baseValueSource, bool isExpression, bool isAnimated, bool isCoerced, bool isCurrent)
    {
        BaseValueSource = baseValueSource;
        IsExpression = isExpression;
        IsAnimated = isAnimated;
        IsCoerced = isCoerced;
        IsCurrent = isCurrent;
    }

    public BaseValueSource BaseValueSource { get; }
    public bool IsExpression { get; }
    public bool IsAnimated { get; }
    public bool IsCoerced { get; }
    public bool IsCurrent { get; }
}

/// <summary>
/// Value-source diagnostics (matches System.Windows.DependencyPropertyHelper):
/// GetValueSource reports the DependencyObject store provenance behind
/// GetValue (Default for unset, Local for SetValue, Inherited when an
/// ancestor holds a local value on an Inherits property, style/trigger
/// flavors when those paths wrote last), with IsExpression from the live
/// binding/dynamic-resource registries and IsAnimated once an animation has
/// produced a value. CLR-field mirrors that bypass the DP store (Control
/// chrome setters) stay invisible here, exactly as they are to GetValue:
/// the DP store is the value authority.
/// IsTemplatedValueDynamic keeps the WPF guard (ArgumentException off a
/// template) and reports TemplateTrigger provenance on templated parts.
/// </summary>
public static class DependencyPropertyHelper
{
    public static ValueSource GetValueSource(DependencyObject dependencyObject, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        ArgumentNullException.ThrowIfNull(dp);
        if (dependencyObject.TryGetSource(dp, out var source, out var coerced, out var current))
        {
            return new ValueSource(source,
                BindingOperations.GetBindingExpressionBase(dependencyObject, dp) is not null ||
                Application.DynamicResourceManager.HasPropertySubscription(dependencyObject, dp),
                WpfAnimationClock.HasProducedValue(dependencyObject, dp),
                coerced, current);
        }
        if (IsInherited(dependencyObject, dp))
            return new ValueSource(BaseValueSource.Inherited, false, false, false, false);
        return new ValueSource(BaseValueSource.Default, false, false, false, false);
    }

    public static bool IsTemplatedValueDynamic(DependencyObject elementInTemplate, DependencyProperty dependencyProperty)
    {
        ArgumentNullException.ThrowIfNull(elementInTemplate);
        ArgumentNullException.ThrowIfNull(dependencyProperty);
        if (elementInTemplate is not FrameworkElement element || element.TemplateParent is null)
            throw new ArgumentException(
                "Element must belong to a FrameworkTemplate instance. (Parameter 'elementInTemplate')");
        return elementInTemplate.TryGetSource(dependencyProperty, out var source, out _, out _) &&
            source == BaseValueSource.TemplateTrigger;
    }

    private static bool IsInherited(DependencyObject target, DependencyProperty dp)
    {
        if (target.HasLocalValue(dp)) return false;
        var metadata = EffectiveMetadataOf(target, dp);
        if (metadata is not FrameworkPropertyMetadata framework || !framework.Inherits) return false;
        for (var current = VisualTreeHelper.GetParent(target);
            current is not null;
            current = VisualTreeHelper.GetParent(current))
        {
            if (current.HasLocalValue(dp)) return true;
        }
        return false;
    }

    private static PropertyMetadata EffectiveMetadataOf(DependencyObject target, DependencyProperty dp)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
            if (DependencyProperty.TryGetOverride(type, dp.Name, out var metadata))
                return metadata;
        return dp.Metadata;
    }
}

/// <summary>Base property metadata: default value plus change callback.</summary>
public class PropertyMetadata
{
    /// <summary>Parameterless metadata (matches PropertyMetadata(); converted vendor
    /// registrations call it explicitly, and optional-parameter ctors do not
    /// satisfy cross-assembly parameterless calls).</summary>
    public PropertyMetadata() : this((object?)null) { }

    public PropertyMetadata(object? defaultValue = null) => DefaultValue = defaultValue;

    public PropertyMetadata(PropertyChangedCallback? propertyChangedCallback)
        : this(null, propertyChangedCallback) { }

    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
        : this(defaultValue, propertyChangedCallback, null) { }

    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
    {
        DefaultValue = defaultValue;
        PropertyChangedCallback = propertyChangedCallback;
        CoerceValueCallback = coerceValueCallback;
    }

    public object? DefaultValue { get; }

    public PropertyChangedCallback? PropertyChangedCallback { get; }

    /// <summary>Coercion hook (matches PropertyMetadata.CoerceValueCallback; applied on SetValue).</summary>
    public CoerceValueCallback? CoerceValueCallback { get; protected set; }
}

/// <summary>
/// UI metadata (matches System.Windows.UIPropertyMetadata): PropertyMetadata
/// plus the animation-prohibition flag. No animation clock exists here, so
/// the flag is retained and queryable (animations never target the property).
/// </summary>
public class UIPropertyMetadata : PropertyMetadata
{
    public UIPropertyMetadata(object? defaultValue = null) : base(defaultValue) { }

    /// <summary>Callback-only registration (matches
    /// System.Windows.UIPropertyMetadata(PropertyChangedCallback); converted
    /// MaterialDesign registrations call it explicitly, and the
    /// optional-parameter ctor above does not satisfy cross-assembly calls).</summary>
    public UIPropertyMetadata(PropertyChangedCallback? propertyChangedCallback)
        : base(null, propertyChangedCallback) { }

    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
        : base(defaultValue, propertyChangedCallback) { }

    /// <summary>
    /// Change-plus-coerce registration (matches System.Windows.UIPropertyMetadata
    /// in PresentationCore 9.0.21; converted AvalonDock layout items register
    /// with this exact shape, and without it their static initializers fail
    /// with MissingMethodException before any element exists).
    /// </summary>
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
        : base(defaultValue, propertyChangedCallback, coerceValueCallback) { }

    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited)
        : base(defaultValue, propertyChangedCallback, coerceValueCallback) => IsAnimationProhibited = isAnimationProhibited;

    public bool IsAnimationProhibited { get; set; }
}

/// <summary>
/// Registration flags (matches System.Windows.FrameworkPropertyMetadataOptions;
/// values verified against WPF: None=0, Measure=1, Arrange=2, ParentMeasure=4,
/// ParentArrange=8, Render=0x10, Inherits=0x20, OverridesInheritance=0x40,
/// NotDataBindable=0x80, BindsTwoWayByDefault=0x100, Journal=0x400,
/// SubPropertiesDoNotAffectRender=0x800).
/// </summary>
[Flags]
public enum FrameworkPropertyMetadataOptions
{
    None = 0,
    AffectsMeasure = 1,
    AffectsArrange = 2,
    AffectsParentMeasure = 4,
    AffectsParentArrange = 8,
    AffectsRender = 16,
    Inherits = 32,
    OverridesInheritanceBehavior = 64,
    NotDataBindable = 128,
    BindsTwoWayByDefault = 256,
    Journal = 1024,
    SubPropertiesDoNotAffectRender = 2048,
}

/// <summary>
/// Layout/render metadata (WPF FrameworkPropertyMetadata shape, WASM-safe).
/// Declares which pipeline stage a <see cref="DependencyProperty"/> change invalidates.
/// Unsealed like real WPF (converted HandyControl Expression shapes derive
/// DrawingPropertyMetadata from it; a sealed engine base fails their load
/// with "parent type is sealed").
/// </summary>
public class FrameworkPropertyMetadata : PropertyMetadata
{
    /// <summary>
    /// Explicit parameterless form (matches
    /// System.Windows.FrameworkPropertyMetadata in PresentationFramework
    /// 9.0.21, which declares this overload alongside the default-valued
    /// shape: converted HandyControl TriggerBase registers its Actions
    /// collection with the exact parameterless MethodDef, and without it the
    /// static initializer fails with MissingMethodException before any
    /// trigger exists; the optional-parameter shape above covers engine
    /// callers but emits no such MethodDef).
    /// </summary>
    public FrameworkPropertyMetadata() : base(null) { }

    public FrameworkPropertyMetadata(object? defaultValue = null) : base(defaultValue) { }

    public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback)
        : base(propertyChangedCallback) { }

    public FrameworkPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
        : base(defaultValue, propertyChangedCallback) { }

    /// <summary>
    /// Change-plus-coerce registration without flags (matches
    /// System.Windows.FrameworkPropertyMetadata in PresentationFramework
    /// 9.0.21; converted AvalonDock layout items register with this exact
    /// shape, and without it their static initializers fail with
    /// MissingMethodException before any element exists).
    /// </summary>
    public FrameworkPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
        : base(defaultValue, propertyChangedCallback, coerceValueCallback) { }

    /// <summary>
    /// Change-plus-coerce registration without a default value (matches
    /// System.Windows.FrameworkPropertyMetadata in PresentationFramework
    /// 9.0.21; same converted-initializer family as the default-valued
    /// shape above).
    /// </summary>
    public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
        : base(null, propertyChangedCallback, coerceValueCallback) { }

    /// <summary>Flags form (matches the original Arrow/Shape registrations).</summary>
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags)
        : this(defaultValue, flags, null) { }

    /// <summary>Flags plus change callback (matches the original registrations).</summary>
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback)
        : base(defaultValue, propertyChangedCallback)
    {
        AffectsMeasure = flags.HasFlag(FrameworkPropertyMetadataOptions.AffectsMeasure);
        AffectsArrange = flags.HasFlag(FrameworkPropertyMetadataOptions.AffectsArrange);
        AffectsRender = flags.HasFlag(FrameworkPropertyMetadataOptions.AffectsRender);
        BindsTwoWayByDefault = flags.HasFlag(FrameworkPropertyMetadataOptions.BindsTwoWayByDefault);
        IsNotDataBindable = flags.HasFlag(FrameworkPropertyMetadataOptions.NotDataBindable);
        Inherits = flags.HasFlag(FrameworkPropertyMetadataOptions.Inherits);
        Journal = flags.HasFlag(FrameworkPropertyMetadataOptions.Journal);
    }

    /// <summary>Full form (matches WPF; coercion runs on SetValue, animation prohibition is retained).</summary>
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
        : this(defaultValue, flags, propertyChangedCallback) => CoerceValueCallback = coerceValueCallback;

    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited)
        : this(defaultValue, flags, propertyChangedCallback, coerceValueCallback) => IsAnimationProhibited = isAnimationProhibited;

    /// <summary>Full form with default trigger (matches the WPF 6-argument shape; converted initializers set it).</summary>
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited, UpdateSourceTrigger defaultUpdateSourceTrigger)
        : this(defaultValue, flags, propertyChangedCallback, coerceValueCallback, isAnimationProhibited) => DefaultUpdateSourceTrigger = defaultUpdateSourceTrigger;

    /// <summary>Change re-runs Measure (implies arrange + repaint).</summary>
    public bool AffectsMeasure { get; set; }

    /// <summary>Change re-runs Arrange (implies repaint).</summary>
    public bool AffectsArrange { get; set; }

    /// <summary>Change repaints only (no layout).</summary>
    public bool AffectsRender { get; set; }

    /// <summary>Two-way-by-default binding (matches WPF; retained, the binding pump reads it).</summary>
    public bool BindsTwoWayByDefault { get; set; }

    /// <summary>Binding veto (matches NotDataBindable; retained).</summary>
    public bool IsNotDataBindable { get; set; }

    /// <summary>Value inheritance (matches Inherits; retained, no inherited store in v1).</summary>
    public bool Inherits { get; set; }

    /// <summary>Journal retention (matches Journal; retained, no journal in v1).</summary>
    public bool Journal { get; set; }

    /// <summary>Animation prohibition (matches IsAnimationProhibited; retained, no clock in v1).</summary>
    public bool IsAnimationProhibited { get; set; }

    /// <summary>Change re-runs the parent Measure (matches WPF; retained).</summary>
    public bool AffectsParentMeasure { get; set; }

    /// <summary>Change re-runs the parent Arrange (matches WPF; retained).</summary>
    public bool AffectsParentArrange { get; set; }

    /// <summary>Inheritance override behavior (matches OverridesInheritanceBehavior; retained).</summary>
    public bool OverridesInheritanceBehavior { get; set; }

    /// <summary>Sub-property changes skip render (matches SubPropertiesDoNotAffectRender; retained).</summary>
    public bool SubPropertiesDoNotAffectRender { get; set; }

    /// <summary>Default update trigger (matches DefaultUpdateSourceTrigger; retained, the binding pump reads it).</summary>
    public UpdateSourceTrigger DefaultUpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;
}

/// <summary>Read-only registration key (matches System.Windows.DependencyPropertyKey).</summary>
public sealed class DependencyPropertyKey
{
    internal DependencyPropertyKey(DependencyProperty property) => DependencyProperty = property;

    public DependencyProperty DependencyProperty { get; }

    public void OverrideMetadata(Type forType, PropertyMetadata typeMetadata) =>
        DependencyProperty.OverrideMetadata(forType, typeMetadata);
}

/// <summary>Minimal dependency property registry (WPF WindowsBase shape, WASM-safe).</summary>
public sealed class DependencyProperty
{
    private static readonly Dictionary<(Type Owner, string Name), DependencyProperty> Registry = new();

    // Per-type metadata overrides (OverrideMetadata/AddOwner with metadata).
    private static readonly Dictionary<(Type Owner, string Name), PropertyMetadata> Overrides = new();

    /// <summary>Clears a local value (matches DependencyProperty.UnsetValue).</summary>
    public static readonly object UnsetValue = new();

    private DependencyProperty(string name, Type propertyType, Type ownerType, PropertyMetadata metadata)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        Metadata = metadata;
    }

    public string Name { get; }
    public Type PropertyType { get; }
    public Type OwnerType { get; }

    /// <summary>Registration metadata: default value plus layout/render effects.</summary>
    public PropertyMetadata Metadata { get; }

    public object? DefaultValue => Metadata.DefaultValue;

    /// <summary>Base registration metadata (matches DefaultMetadata).</summary>
    public PropertyMetadata DefaultMetadata => Metadata;

    /// <summary>Set through a DependencyPropertyKey only (matches IsReadOnly).</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Read-only marker (matches DependencyProperty.ReadOnly, the public
    /// getter converted AvalonDock's SyncInheritedProperty guards on before
    /// mirroring an inherited value: same flag as IsReadOnly, like WPF).
    /// </summary>
    public bool ReadOnly => IsReadOnly;

    /// <summary>
    /// Effective metadata for a type (matches GetMetadata(Type)): walks the
    /// override chain from the given type, falling back to the base
    /// registration (converted AvalonDock filters inheritable properties
    /// through this, like WPF).
    /// </summary>
    public PropertyMetadata GetMetadata(Type forType)
    {
        ArgumentNullException.ThrowIfNull(forType);
        for (var type = forType; type is not null; type = type.BaseType)
            if (TryGetOverride(type, Name, out var metadata))
                return metadata;
        return Metadata;
    }

    /// <summary>
    /// Effective metadata for an instance's type (matches
    /// GetMetadata(DependencyObject)): same override walk, seeded at the
    /// runtime type (converted AvalonDock's OnPropertyChanged narrows
    /// through this, like WPF).
    /// </summary>
    public PropertyMetadata GetMetadata(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return GetMetadata(dependencyObject.GetType());
    }

    /// <summary>Attached registration (matches the WPF attached-property shape; backs qualified style setters).</summary>
    public bool IsAttached { get; private set; }

    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, object? defaultValue = null) =>
        Register(name, propertyType, ownerType, new PropertyMetadata(defaultValue));

    /// <summary>
    /// Three-argument registration (matches real WPF, which declares this
    /// overload alongside the default-valued shape: converted OxyPlot.Wpf
    /// PlotViewBase static initializers bind the exact 3-arg MethodDef, and
    /// without it instantiation fails with MissingMethodException even
    /// though the 4-arg optional-parameter shape covers engine callers).
    /// </summary>
    public static DependencyProperty Register(string name, Type propertyType, Type ownerType) =>
        Register(name, propertyType, ownerType, new PropertyMetadata(null));

    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var key = (ownerType, name);
        lock (Registry)
        {
            if (!Registry.TryGetValue(key, out var dp))
            {
                dp = new DependencyProperty(name, propertyType, ownerType, metadata);
                Registry[key] = dp;
            }

            return dp;
        }
    }

    /// <summary>Attached registration with default metadata (matches RegisterAttached(name, type, owner)).</summary>
    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType) =>
        RegisterAttached(name, propertyType, ownerType, new PropertyMetadata(null));

    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, object? defaultValue = null) =>
        RegisterAttached(name, propertyType, ownerType, new PropertyMetadata(defaultValue));

    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata metadata)
    {
        var dp = Register(name, propertyType, ownerType, metadata);
        dp.IsAttached = true;
        return dp;
    }

    public static DependencyPropertyKey RegisterReadOnly(string name, Type propertyType, Type ownerType, PropertyMetadata metadata)
    {
        var dp = Register(name, propertyType, ownerType, metadata);
        dp.IsReadOnly = true;
        return new DependencyPropertyKey(dp);
    }

    /// <summary>Read-only attached registration (matches RegisterAttachedReadOnly).</summary>
    public static DependencyPropertyKey RegisterAttachedReadOnly(string name, Type propertyType, Type ownerType, PropertyMetadata metadata)
    {
        var dp = RegisterAttached(name, propertyType, ownerType, metadata);
        dp.IsReadOnly = true;
        return new DependencyPropertyKey(dp);
    }

    /// <summary>Validated registration (matches WPF; invalid values throw on SetValue).</summary>
    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata metadata, ValidateValueCallback validateValueCallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(validateValueCallback);
        if (!validateValueCallback(metadata.DefaultValue))
            throw new ArgumentException($"Default value for '{name}' property is not valid because ValidateValueCallback failed.", nameof(metadata));
        var dp = Register(name, propertyType, ownerType, metadata);
        lock (Registry) { Validators[(ownerType, name)] = validateValueCallback; }
        return dp;
    }

    /// <summary>Validated attached registration (matches WPF).</summary>
    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata metadata, ValidateValueCallback validateValueCallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(validateValueCallback);
        if (!validateValueCallback(metadata.DefaultValue))
            throw new ArgumentException($"Default value for '{name}' property is not valid because ValidateValueCallback failed.", nameof(metadata));
        var dp = RegisterAttached(name, propertyType, ownerType, metadata);
        lock (Registry) { Validators[(ownerType, name)] = validateValueCallback; }
        return dp;
    }

    private static readonly Dictionary<(Type Owner, string Name), ValidateValueCallback> Validators = new();

    internal static ValidateValueCallback? TryGetValidator(Type owner, string name)
    {
        lock (Registry)
        {
            return Validators.TryGetValue((owner, name), out var validator) ? validator : null;
        }
    }

    /// <summary>
    /// Finds the "nameProperty" static field walking up the owner hierarchy
    /// (style setters resolve through this; XAML parsing registers a matching
    /// property when only a CLR property exists).
    /// Public fields win; non-public (internal/private) fields resolve as a
    /// fallback because vendor theme dictionaries reference same-assembly
    /// internal DPs (e.g. MaterialDesign TextFieldAssist.TrailingContent),
    /// which native XAML accepts from within the defining assembly.
    /// </summary>
    public static DependencyProperty? FromName(Type ownerType, string name)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentException.ThrowIfNullOrEmpty(name);
        for (var t = ownerType; t is not null && t != typeof(object); t = t.BaseType)
        {
            var field = t.GetField(name + "Property",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dp) return dp;
        }
        for (var t = ownerType; t is not null && t != typeof(object); t = t.BaseType)
        {
            var field = t.GetField(name + "Property",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dp) return dp;
        }
        return null;
    }

    /// <summary>Registers this property for another owner (same instance, like WPF).</summary>
    public DependencyProperty AddOwner(Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        lock (Registry)
        {
            Registry[(ownerType, Name)] = this;
            return this;
        }
    }

    public DependencyProperty AddOwner(Type ownerType, PropertyMetadata typeMetadata)
    {
        AddOwner(ownerType);
        OverrideMetadata(ownerType, typeMetadata);
        return this;
    }

    public void OverrideMetadata(Type forType, PropertyMetadata typeMetadata)
    {
        ArgumentNullException.ThrowIfNull(forType);
        ArgumentNullException.ThrowIfNull(typeMetadata);
        lock (Registry) { Overrides[(forType, Name)] = typeMetadata; }
    }

    internal static bool TryGetOverride(Type owner, string name, out PropertyMetadata metadata)
    {
        lock (Registry) { return Overrides.TryGetValue((owner, name), out metadata!); }
    }
}

/// <summary>Minimal DependencyObject with local value store + change notification.</summary>
public class DependencyObject
{
    private Dictionary<DependencyProperty, object?> _local = new();

    /// <summary>
    /// Provenance of the current local entries (DependencyPropertyHelper
    /// surface): Local for SetValue, style/trigger flavors when those paths
    /// wrote last, plus coercion/current flags. Absent means untagged Local
    /// for entries written before tagging existed (never for new writes).
    /// </summary>
    private Dictionary<DependencyProperty, (BaseValueSource Source, bool Coerced, bool Current)>? _sources;

    /// <summary>
    /// Gives a MemberwiseClone its own local-value store (supports
    /// Freezable.Clone; the shared reference would alias the original).
    /// </summary>
    protected void ForkLocalValues()
    {
        _local = new Dictionary<DependencyProperty, object?>(_local);
        if (_sources is not null)
            _sources = new Dictionary<DependencyProperty, (BaseValueSource, bool, bool)>(_sources);
    }

    /// <summary>
    /// Provenance probe for DependencyPropertyHelper (internal): the tagged
    /// source of the current local entry, or false when unset.
    /// </summary>
    internal bool TryGetSource(DependencyProperty dp, out BaseValueSource source, out bool coerced, out bool current)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (_local.ContainsKey(dp))
        {
            if (_sources is not null && _sources.TryGetValue(dp, out var record))
            {
                (source, coerced, current) = record;
                return true;
            }
            (source, coerced, current) = (BaseValueSource.Local, false, false);
            return true;
        }
        (source, coerced, current) = (BaseValueSource.Default, false, false);
        return false;
    }

    /// <summary>
    /// Tags the current local entry with a non-local provenance (style,
    /// trigger and template paths call this after writing; plain SetValue
    /// always records Local). No local entry means no tag: the value is the
    /// default, and the default source stands.
    /// </summary>
    internal void SetSourceTag(DependencyProperty dp, BaseValueSource source)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!_local.ContainsKey(dp)) return;
        _sources ??= new Dictionary<DependencyProperty, (BaseValueSource, bool, bool)>();
        var coerced = _sources.TryGetValue(dp, out var existing) && existing.Coerced;
        _sources[dp] = (source, coerced, false);
    }

    public event Action<DependencyProperty, object?, object?>? PropertyChanged;

    public object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _local.TryGetValue(dp, out var v) ? v : GetDefault(dp);
    }

    /// <summary>Local-value probe (matches ReadLocalValue != UnsetValue; backs inherited attached reads).</summary>
    internal bool HasLocalValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _local.ContainsKey(dp);
    }

    private object? GetDefault(DependencyProperty dp) => GetEffectiveMetadata(dp).DefaultValue;

    public void SetValue(DependencyProperty dp, object? value) =>
        WriteLocal(dp, value, BaseValueSource.Local, current: false);

    /// <summary>
    /// Style/trigger/template write path (internal): the same effective-value
    /// write as SetValue, tagged with the real provenance so
    /// DependencyPropertyHelper reports Style/StyleTrigger/TemplateTrigger/
    /// DefaultStyle instead of Local.
    /// </summary>
    internal void SetValueWithSource(DependencyProperty dp, object? value, BaseValueSource source) =>
        WriteLocal(dp, value, source, current: false);

    private void WriteLocal(DependencyProperty dp, object? value, BaseValueSource source, bool current)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfSetBlocked(dp);
        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            ClearValue(dp);
            return;
        }
        var (coercedValue, coercedChanged) = CoerceAndValidate(dp, value);
        Store(dp, coercedValue, coercedChanged, source, current);
    }

    /// <summary>
    /// Commits an already validated/coerced value: unchanged state stays
    /// silent, otherwise exactly one notification fires (shared by the
    /// ordinary and the keyed write paths so both announce identically).
    /// </summary>
    private void Store(DependencyProperty dp, object? coercedValue, bool coercedChanged, BaseValueSource source, bool current)
    {
        var old = GetValue(dp);
        _local[dp] = coercedValue;
        _sources ??= new Dictionary<DependencyProperty, (BaseValueSource, bool, bool)>();
        _sources[dp] = (source, coercedChanged, current);
        if (Equals(old, coercedValue)) return;
        Announce(dp, old, coercedValue);
    }

    /// <summary>
    /// Type guard for every value-accepting write (matches native WPF: a
    /// wrong-typed value throws ArgumentException before the registration
    /// validator runs, and null is rejected for non-nullable value types
    /// while staying legal for reference and Nullable types).
    /// </summary>
    private static void ThrowIfBadType(DependencyProperty dp, object? value)
    {
        var propertyType = dp.PropertyType;
        if (value is null)
        {
            if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
                return;
            throw new ArgumentException($"Null is not a valid value for property '{dp.Name}'.", nameof(value));
        }
        if (!propertyType.IsInstanceOfType(value))
            throw new ArgumentException($"'{value}' is not a valid value for property '{dp.Name}'.", nameof(value));
    }

    /// <summary>
    /// Runs the type guard on the incoming value first, then the
    /// registration validator, then the effective coerce hook (matches
    /// native WPF ordering, verified against the ParityProbe oracle: a
    /// wrong-typed or invalid value throws ArgumentException before the
    /// coerce callback runs); reports whether coercion changed the value
    /// (live-verified: in-range coerced writes report IsCoerced false,
    /// out-of-range true).
    /// </summary>
    private (object? Value, bool CoercedChanged) CoerceAndValidate(DependencyProperty dp, object? value)
    {
        ThrowIfBadType(dp, value);
        var validator = DependencyProperty.TryGetValidator(dp.OwnerType, dp.Name);
        if (validator is not null && !validator(value!))
            throw new ArgumentException($"'{value}' is not a valid value for property '{dp.Name}'.", nameof(value));
        var metadata = GetEffectiveMetadata(dp);
        var changed = false;
        if (metadata.CoerceValueCallback is not null)
        {
            var coerced = metadata.CoerceValueCallback(this, value!);
            changed = !Equals(value, coerced);
            value = coerced;
        }
        return (value, changed);
    }

    /// <summary>Privileged write for read-only properties (matches SetValue(key)).</summary>
    public void SetValue(DependencyPropertyKey key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (this is Freezable { IsFrozen: true })
            throw new InvalidOperationException($"Cannot set '{key.DependencyProperty.Name}': '{GetType().Name}' is frozen.");
        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            ClearValue(key);
            return;
        }
        var dp = key.DependencyProperty;
        var (coercedValue, coercedChanged) = CoerceAndValidate(dp, value);
        Store(dp, coercedValue, coercedChanged, BaseValueSource.Local, current: false);
    }

    public void ClearValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (this is Freezable { IsFrozen: true })
            throw new InvalidOperationException($"Cannot clear '{dp.Name}': '{GetType().Name}' is frozen.");
        if (dp.IsReadOnly)
            throw new InvalidOperationException($"'{dp.Name}' is read-only: clear it through its DependencyPropertyKey.");
        ClearValueCore(dp);
    }

    /// <summary>Privileged clear for read-only properties (matches ClearValue(key)).</summary>
    public void ClearValue(DependencyPropertyKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var dp = key.DependencyProperty;
        if (this is Freezable { IsFrozen: true })
            throw new InvalidOperationException($"Cannot clear '{dp.Name}': '{GetType().Name}' is frozen.");
        ClearValueCore(dp);
    }

    private void ClearValueCore(DependencyProperty dp)
    {
        if (!_local.Remove(dp, out var old)) return;
        _sources?.Remove(dp);
        var current = GetDefault(dp);
        if (Equals(old, current)) return;
        Announce(dp, old, current);
    }

    /// <summary>
    /// Current-value write (matches SetCurrentValue): the effective value
    /// changes without touching the provenance a SetValue would overwrite,
    /// so DependencyPropertyHelper reports Local with IsCoerced and IsCurrent
    /// (live-verified against the 9.0.21 runtime).
    /// </summary>
    public void SetCurrentValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfSetBlocked(dp);
        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            ClearValue(dp);
            return;
        }
        var (coercedValue, _) = CoerceAndValidate(dp, value);
        var old = GetValue(dp);
        _local[dp] = coercedValue;
        _sources ??= new Dictionary<DependencyProperty, (BaseValueSource, bool, bool)>();
        _sources[dp] = (BaseValueSource.Local, true, true);
        if (Equals(old, coercedValue)) return;
        Announce(dp, old, coercedValue);
    }

    /// <summary>
    /// Re-evaluation hook; a no-op until coercion/binding sources land
    /// (kept so converted callers bind).
    /// </summary>
    public void InvalidateProperty(DependencyProperty dp) => ArgumentNullException.ThrowIfNull(dp);

    /// <summary>
    /// Re-runs the registration coerce callback over the current value
    /// (matches DependencyObject.CoerceValue; converted HandyControl
    /// editors call it after clamping their range inputs). Provenance is
    /// preserved: only the coerced flag refreshes, like a SetValue that
    /// never overwrites the source.
    /// </summary>
    public void CoerceValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        var source = BaseValueSource.Local;
        var current = false;
        if (_sources is not null && _sources.TryGetValue(dp, out var existing))
            (source, _, current) = existing;
        WriteLocal(dp, GetValue(dp), source, current);
    }

    private void ThrowIfSetBlocked(DependencyProperty dp)
    {
        if (this is Freezable { IsFrozen: true })
            throw new InvalidOperationException($"Cannot set '{dp.Name}': '{GetType().Name}' is frozen.");
        if (dp.IsReadOnly)
            throw new InvalidOperationException($"'{dp.Name}' is read-only: set it through its DependencyPropertyKey.");
    }

    private PropertyMetadata GetEffectiveMetadata(DependencyProperty dp)
    {
        for (var type = GetType(); type is not null; type = type.BaseType)
            if (DependencyProperty.TryGetOverride(type, dp.Name, out var metadata))
                return metadata;
        return dp.Metadata;
    }

    private void Announce(DependencyProperty dp, object? oldValue, object? newValue)
    {
        var args = new DependencyPropertyChangedEventArgs(dp, oldValue, newValue);
        GetEffectiveMetadata(dp).PropertyChangedCallback?.Invoke(this, args);
        PropertyChanged?.Invoke(dp, oldValue, newValue);
        OnPropertyChanged(args);
    }

    /// <summary>
    /// Standard change hook (matches DependencyObject.OnPropertyChanged);
    /// forwards to the engine invalidation hook so existing overrides keep
    /// working.
    /// </summary>
    protected virtual void OnPropertyChanged(DependencyPropertyChangedEventArgs e) =>
        OnPropertyChanged(e.Property, e.OldValue, e.NewValue);

    /// <summary>
    /// Post-notification hook. <see cref="UIElement"/> overrides it to turn
    /// <see cref="FrameworkPropertyMetadata"/> effects into layout/render invalidation,
    /// so programmatic setters reach the next frame without explicit host Invalidate().
    /// </summary>
    protected virtual void OnPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue) { }
}

/// <summary>Freezable: sealing plus shallow clone (single-threaded WASM).</summary>
public abstract class Freezable : DependencyObject
{
    public bool IsFrozen { get; private set; }
    public void Freeze() => IsFrozen = true;

    /// <summary>
    /// Builds the instance Clone and frozen copies derive from (matches
    /// Freezable.CreateInstanceCore; converted timeline overrides bind here).
    /// </summary>
    protected virtual Freezable CreateInstanceCore() => Clone();

    /// <summary>Always true here (matches CanFreeze; no cross-thread state).</summary>
    public bool CanFreeze => !IsFrozen;

    /// <summary>Modifiable shallow copy (matches Clone).</summary>
    public Freezable Clone()
    {
        var clone = (Freezable)MemberwiseClone();
        clone.ForkLocalValues();
        clone.IsFrozen = false;
        return clone;
    }

    /// <summary>Identical to Clone here (no per-frame state to snapshot).</summary>
    public Freezable CloneCurrentValue() => Clone();

    /// <summary>Frozen copy, or this instance when already frozen.</summary>
    public Freezable GetAsFrozen()
    {
        if (IsFrozen) return this;
        var clone = Clone();
        clone.Freeze();
        return clone;
    }

    /// <summary>Identical to GetAsFrozen here (no per-frame state to snapshot).</summary>
    public Freezable GetCurrentValueAsFrozen() => GetAsFrozen();

    protected void ThrowIfFrozen()
    {
        if (IsFrozen) throw new InvalidOperationException("Object is frozen.");
    }

    /// <summary>
    /// Read access check (matches System.Windows.Freezable.ReadPreamble;
    /// converted interactivity attach paths call it through TriggerBase and
    /// AttachableCollection; the engine is single-threaded so this is a
    /// no-op like the frozen-thread contract it stands in for).
    /// </summary>
    protected void ReadPreamble()
    {
    }

    /// <summary>
    /// Write access check (matches System.Windows.Freezable.WritePreamble;
    /// throws when frozen, like WPF, so converted attach paths fail loud
    /// instead of mutating frozen collections).
    /// </summary>
    protected void WritePreamble() => ThrowIfFrozen();

    /// <summary>
    /// Write completion (matches System.Windows.Freezable.WritePostscript;
    /// no-op here: the DP store has no cached-state invalidation to run).
    /// </summary>
    protected void WritePostscript()
    {
    }
}
