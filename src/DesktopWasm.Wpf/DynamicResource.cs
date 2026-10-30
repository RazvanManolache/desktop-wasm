using System.Reflection;

namespace DesktopWasm.Wpf;

// Live DynamicResource machinery (PresentationFramework/System.Xaml
// subset, shim, not port): the markup-extension contract converted
// ScreenToGif.Util code binds against (DynamicResourceBinding derives
// DynamicResourceExtension, overrides ProvideValue, and calls back into
// base.ProvideValue plus Binding.ProvideValue with an
// IProvideValueTarget service). Member names and signatures match WPF
// (verified against the Microsoft.WindowsDesktop.App.Ref assemblies);
// resolution and refresh ride the engine DynamicResourceManager, so a
// resource swap re-applies live dependents and repaints them.

/// <summary>Markup value contract (matches System.Windows.Markup.MarkupExtension).</summary>
public abstract class MarkupExtension
{
    /// <summary>Produces the value for a XAML/code target.</summary>
    public abstract object? ProvideValue(IServiceProvider serviceProvider);
}

/// <summary>
/// Return-type annotation (matches
/// System.Windows.Markup.MarkupExtensionReturnTypeAttribute, verified live
/// against the 9.0.21 System.Xaml ref): converted AvalonDock theme
/// extensions carry [MarkupExtensionReturnType(typeof(VsTheme))], which the
/// converter keeps verbatim with its typeof argument rewritten. The
/// parameterless and (Type, Type) overloads plus the obsolete
/// ExpressionType marker ride along so every framework-shaped use binds.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MarkupExtensionReturnTypeAttribute : Attribute
{
    public MarkupExtensionReturnTypeAttribute()
    {
    }

    public MarkupExtensionReturnTypeAttribute(Type returnType) => ReturnType = returnType;

    public MarkupExtensionReturnTypeAttribute(Type returnType, Type expressionType)
    {
        ReturnType = returnType;
#pragma warning disable CS0618 // ExpressionType is obsolete in WPF too; the (Type, Type) overload keeps setting it.
        ExpressionType = expressionType;
#pragma warning restore CS0618
    }

    /// <summary>Value type the annotated extension produces.</summary>
    public Type? ReturnType { get; }

    /// <summary>Expression type (obsolete in WPF; kept for binding parity).</summary>
    [Obsolete("This is not used by the XAML parser. Please look at XamlSetMarkupExtensionAttribute.")]
    public Type? ExpressionType { get; }
}

/// <summary>
/// XML language tag (matches System.Windows.Markup.XmlLanguage).
/// Slice-69 MaterialDesignDemo entry prerequisite (the demo reads
/// XmlLanguage.GetLanguage plus GetSpecificCulture off its resources).
/// Tags canonicalize to lowercase ("en-US" -&gt; "en-us"), Empty carries
/// "", GetLanguage(null) throws ArgumentNullException and the empty tag
/// parses (all verified live against the 9.0.21 runtime). Instances are
/// cached per canonical tag, so repeated lookups share the reference.
/// </summary>
public sealed class XmlLanguage : IEquatable<XmlLanguage>
{
    private static readonly Dictionary<string, XmlLanguage> Cache = new(StringComparer.Ordinal);

    private XmlLanguage(string tag) => IetfLanguageTag = tag;

    /// <summary>Empty tag (matches XmlLanguage.Empty; carries "").</summary>
    public static XmlLanguage Empty => GetLanguage("");

    /// <summary>Canonical BCP-47 tag (lowercase, like WPF).</summary>
    public string IetfLanguageTag { get; }

    /// <summary>Looks up (and caches) the language for a tag (matches XmlLanguage.GetLanguage).</summary>
    public static XmlLanguage GetLanguage(string ietfLanguageTag)
    {
        ArgumentNullException.ThrowIfNull(ietfLanguageTag);
        var canonical = ietfLanguageTag.Replace('_', '-').ToLowerInvariant();
        // Validates like WPF: unknown tags fail when the culture resolves.
        _ = new System.Globalization.CultureInfo(canonical);
        lock (Cache)
        {
            if (!Cache.TryGetValue(canonical, out var language))
            {
                language = new XmlLanguage(canonical);
                Cache[canonical] = language;
            }
            return language;
        }
    }

    /// <summary>Closest specific culture (matches XmlLanguage.GetSpecificCulture).</summary>
    public System.Globalization.CultureInfo GetSpecificCulture() =>
        System.Globalization.CultureInfo.CreateSpecificCulture(IetfLanguageTag);

    /// <summary>Culture for the tag itself (matches XmlLanguage.GetEquivalentCulture).</summary>
    public System.Globalization.CultureInfo GetEquivalentCulture() =>
        new(IetfLanguageTag);

    public bool Equals(XmlLanguage? other) =>
        other is not null && IetfLanguageTag == other.IetfLanguageTag;
    public override bool Equals(object? obj) => obj is XmlLanguage other && Equals(other);
    public override int GetHashCode() => IetfLanguageTag.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => IetfLanguageTag;
}

/// <summary>XAML target contract (matches System.Windows.Markup.IProvideValueTarget).</summary>
public interface IProvideValueTarget
{
    /// <summary>Object the markup value lands on.</summary>
    object TargetObject { get; }

    /// <summary>Target property (a DependencyProperty or PropertyInfo).</summary>
    object TargetProperty { get; }
}

/// <summary>
/// Deferred resource reference (matches System.Windows.DynamicResourceExtension):
/// resolves the key through element/ancestor/application scopes and
/// re-applies it when resources change (missing keys stay null, like the
/// engine's other DynamicResource paths, instead of throwing).
/// </summary>
public class DynamicResourceExtension : MarkupExtension
{
    /// <summary>Creates an extension with no key (set ResourceKey before use).</summary>
    public DynamicResourceExtension()
    {
    }

    /// <summary>Creates an extension for a resource key.</summary>
    public DynamicResourceExtension(object resourceKey) => ResourceKey = resourceKey;

    /// <summary>Resource key to resolve (string keys, like XAML).</summary>
    public object? ResourceKey { get; set; }

    /// <summary>
    /// Resolves the key now, lands it on a FrameworkElement target, and
    /// subscribes the target property so resource swaps re-apply (and
    /// repaint) the dependent. No usable target returns the value with no
    /// subscription (setter/deferred contexts, like WPF).
    /// </summary>
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var key = ResourceKey ?? throw new InvalidOperationException(
            "DynamicResourceExtension.ResourceKey is null.");
        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var resolved = Resolve(key, target?.TargetObject as DependencyObject);
        if (target?.TargetObject is FrameworkElement element)
        {
            if (target.TargetProperty is DependencyProperty dp)
            {
                ApplyTo(element, dp, resolved);
                Application.DynamicResourceManager.SubscribeProperty(element, dp, key,
                    (refreshed, value) => ApplyTo(refreshed, dp, value));
            }
            else if (target.TargetProperty is PropertyInfo property && property.CanWrite)
            {
                var converted = ConvertFor(property, resolved);
                property.SetValue(element, converted);
                Application.DynamicResourceManager.SubscribeProperty(element, property, key,
                    (refreshed, value) => property.SetValue(refreshed, ConvertFor(property, value)));
            }
        }
        return resolved;
    }

    private static object? Resolve(object key, DependencyObject? target)
    {
        if (target is FrameworkElement element && element.TryFindResource(key, out var local))
            return local;
        if (Application.Current?.TryFindResource(key, out var appValue) == true)
            return appValue;
        return null;
    }

    private static void ApplyTo(FrameworkElement element, DependencyProperty dp, object? value)
    {
        if (value is null)
        {
            element.ClearValue(dp);
            return;
        }
        // Theme dictionaries store SolidColorBrush entries as packed engine
        // ARGB (uint) or Color (the gallery legacy store); brush/color DPs
        // take them as brushes, the Setter.CoerceResourceValue rule, so a
        // DynamicResource Background/Foreground lands the real upstream brush
        // instead of skipping. Genuine mismatches stay unset (documented
        // tolerance, same as missing keys) instead of corrupting the target.
        if (!dp.PropertyType.IsInstanceOfType(value))
        {
            value = CoerceBrushValue(dp, value);
            if (value is null || !dp.PropertyType.IsInstanceOfType(value)) return;
        }
        element.SetValue(dp, value);
    }

    private static object? CoerceBrushValue(DependencyProperty dp, object? value) =>
        (value, dp.PropertyType) switch
        {
            (Color color, var target) when target.IsAssignableFrom(typeof(SolidColorBrush)) =>
                new SolidColorBrush(color),
            (uint rgba, var target) when target.IsAssignableFrom(typeof(SolidColorBrush)) =>
                new SolidColorBrush(StyleValueConverter.ColorFromRgba(rgba)),
            (uint rgba, var target) when target.IsAssignableFrom(typeof(Color)) =>
                StyleValueConverter.ColorFromRgba(rgba),
            _ => null,
        };

    private static object? ConvertFor(PropertyInfo property, object? value)
    {
        if (value is null)
            return property.PropertyType.IsValueType &&
                Nullable.GetUnderlyingType(property.PropertyType) is null
                ? Activator.CreateInstance(property.PropertyType)
                : null;
        if (property.PropertyType.IsInstanceOfType(value)) return value;
        try
        {
            return System.Convert.ChangeType(value, property.PropertyType,
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException
            or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Resource value '{value}' does not fit '{property.DeclaringType?.Name}.{property.Name}'.", ex);
        }
    }
}

/// <summary>
/// String-serialization marker (matches System.Windows.Markup.ValueSerializerAttribute):
/// converted ScreenToGif.Util.SimpleKeyGesture carries it naming the
/// framework KeyGestureValueSerializer. The engine keeps the annotation
/// with WPF-identical shape so the converted type binds; no XAML
/// serializer consumes it in the browser (honest shim, never fabricated).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public sealed class ValueSerializerAttribute : Attribute
{
    /// <summary>Marks the type with a value-serializer type.</summary>
    public ValueSerializerAttribute(Type valueSerializerType)
    {
        ValueSerializerType = valueSerializerType ?? throw new ArgumentNullException(nameof(valueSerializerType));
        ValueSerializerTypeName = valueSerializerType.AssemblyQualifiedName;
    }

    /// <summary>Marks the type with a value-serializer type name.</summary>
    public ValueSerializerAttribute(string valueSerializerTypeName)
    {
        ValueSerializerTypeName = valueSerializerTypeName ?? throw new ArgumentNullException(nameof(valueSerializerTypeName));
    }

    /// <summary>Serializer type (resolved from the name when only a name was given).</summary>
    public Type? ValueSerializerType { get; }

    /// <summary>Serializer type name.</summary>
    public string? ValueSerializerTypeName { get; }
}
