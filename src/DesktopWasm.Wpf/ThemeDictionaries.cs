using System.Reflection;

namespace DesktopWasm.Wpf;

/// <summary>
/// Converted-assembly theme dictionaries (the WPF generic.xaml default-style
/// contract): loads a staged converted assembly's embedded
/// Themes/Generic.xaml once, then resolves implicit styles by DefaultStyleKey
/// with a base-type walk at template-apply time. Converted controls whose
/// Template comes only from their default style (OxyPlot PlotViewBase, whose
/// Template provides PART_Grid) inflate through the normal engine template
/// path once the style applies; controls with an explicit Style or Template
/// are never touched, and assemblies without a generic.xaml stay a no-op.
/// </summary>
internal static class ThemeDictionaries
{
    /// <summary>Embedded generic.xaml name (what the converter stores loose .xaml under).</summary>
    internal const string GenericXamlName = "Themes/Generic.xaml";

    private static readonly object Gate = new();
    private static readonly Dictionary<Assembly, ResourceDictionary?> Cache = new();

    /// <summary>
    /// Loads (once per assembly) the embedded Themes/Generic.xaml theme
    /// dictionary, or null when the assembly ships none. Parsing runs with
    /// the theme's own assembly as the converted-type scope, so clr-namespace
    /// entries (styles, converters, template parts) resolve there.
    /// </summary>
    internal static ResourceDictionary? EnsureForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            if (Cache.TryGetValue(assembly, out var cached)) return cached;
        }
        ResourceDictionary? parsed = null;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n =>
            n.EndsWith(GenericXamlName, StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Theme resource '{name}' has no stream.");
            using var reader = new StreamReader(stream);
            var previous = XamlLoader.CustomElementSource;
            XamlLoader.CustomElementSource = assembly;
            try
            {
                parsed = XamlLoader.LoadResourceDictionary(reader.ReadToEnd());
            }
            finally
            {
                XamlLoader.CustomElementSource = previous;
            }
        }
        lock (Gate)
        {
            Cache[assembly] = parsed;
            return parsed;
        }
    }

    /// <summary>
    /// Resolves the default style for an element by DefaultStyleKey with a
    /// base-type walk (most-derived first), mirroring WPF's theme-style
    /// lookup. The key defaults to the element type when unset; a non-Type
    /// key never matches.
    /// </summary>
    internal static bool TryFindDefaultStyle(FrameworkElement element, out Style? style)
    {
        ArgumentNullException.ThrowIfNull(element);
        style = null;
        var key = element.GetValue(FrameworkElement.DefaultStyleKeyProperty);
        Type? keyType = key as Type ?? (key is null ? element.GetType() : null);
        if (keyType is null) return false;
        for (var t = keyType; t is not null && t != typeof(object); t = t.BaseType)
        {
            var dict = EnsureForAssembly(t.Assembly);
            if (dict is null) continue;
            if (dict.TryGetValue(t, out var candidate) && candidate is Style found &&
                found.TargetType is not null && found.TargetType.IsInstanceOfType(element))
            {
                style = found;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies the DefaultStyleKey theme style when the element carries no
    /// explicit Style and (for Controls) no Template. Explicit values keep
    /// WPF precedence: this never overwrites them.
    /// </summary>
    internal static void ApplyDefaultStyle(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Style is not null) return;
        if (element is Control control && control.Template is not null) return;
        if (TryFindDefaultStyle(element, out var style) && style is not null)
        {
            style.ApplyTo(element, new StyleApplyContext { Source = BaseValueSource.DefaultStyle });
            return;
        }
        // Converted Card-shaped controls (CardDemo hc:Card): the lib default
        // style ships BAML-only behind the line-17 SkinDefault stop, so the
        // engine carries the upstream template structure as the converted
        // default (see Slice107HandyCard). Explicit styles/templates keep
        // precedence: this only runs when both are null.
        if (element is Control unstyled)
            Slice107HandyCard.TryApplyCardDefault(unstyled);
    }

    /// <summary>
    /// Looks up a theme resource (converter, brush) for a templated control
    /// by walking its type chain, so template TemplateBinding converters
    /// resolve from the defining assembly's generic.xaml like WPF resolves
    /// theme-style StaticResource against the theme dictionary itself.
    /// </summary>
    internal static bool TryFindThemeValue(FrameworkElement control, object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(key);
        for (var t = control.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            var dict = EnsureForAssembly(t.Assembly);
            if (dict is not null && dict.TryGetValue(key, out value) && value is not null)
                return true;
        }
        value = null;
        return false;
    }
}
