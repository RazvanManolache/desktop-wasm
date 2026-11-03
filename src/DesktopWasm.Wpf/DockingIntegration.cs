using System.Reflection;

namespace DesktopWasm.Wpf;

/// <summary>
/// RVW0900 narrow factory for the converted AvalonDock DockingManager.
/// Exact-match only: the declared joint-closure member
/// (assembly "AvalonDock", full name "AvalonDock.DockingManager") resolves
/// from the already-loaded closure assembly and constructs the real
/// converted instance. Anything else — unknown names, engine names, other
/// AvalonDock types — resolves to null so the caller keeps its own precise
/// boundary; no broad assembly scan, no substitute panel, no swallowed
/// constructor failures.
/// </summary>
internal static class DockingIntegration
{
    internal const string DockingManagerFullName = "AvalonDock.DockingManager";
    internal const string DockingManagerAssemblyName = "AvalonDock";
    internal const string TestUserControlFullName = "TestApp.TestUserControl";
    internal const string TestUserControlAssemblyName = "TestApp";

    /// <summary>Exact element-name gate (full CLR name plus the declared
    /// joint-closure assembly record). The assembly record carries version/
    /// culture/token suffixes, so the match is a name prefix on the simple
    /// assembly name, never a substring of the type name.</summary>
    internal static bool IsDockingManagerElement(string typeName, string assemblyRecord)
    {
        if (!string.Equals(typeName, DockingManagerFullName, StringComparison.Ordinal))
            return false;
        var simple = assemblyRecord.Split(',')[0].Trim();
        return string.Equals(simple, DockingManagerAssemblyName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the converted DockingManager type from the
    /// already-loaded joint-closure assembly only. Returns null when the
    /// closure is not loaded (the caller names that boundary); never probes
    /// unrelated assemblies for a same-named private type.</summary>
    internal static Type? TryResolve(string fullName)
    {
        if (!string.Equals(fullName, DockingManagerFullName, StringComparison.Ordinal))
            return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, DockingManagerAssemblyName, StringComparison.OrdinalIgnoreCase))
                continue;
            var direct = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (direct is not null)
                return direct;
        }
        return null;
    }

    /// <summary>Exact element-name gate for the converted TestApp
    /// TestUserControl (0x1F75 wall): full CLR name plus the simple
    /// TestApp assembly record, same exactness as the DockingManager
    /// gate; never a substring or fallback scan.</summary>
    internal static bool IsTestUserControlElement(string typeName, string assemblyRecord)
    {
        if (!string.Equals(typeName, TestUserControlFullName, StringComparison.Ordinal))
            return false;
        var simple = assemblyRecord.Split(',')[0].Trim();
        return string.Equals(simple, TestUserControlAssemblyName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the converted TestUserControl type from the
    /// already-loaded joint-closure assembly only. Returns null when the
    /// closure is not loaded (the caller names that boundary); never probes
    /// unrelated assemblies for a same-named private type.</summary>
    internal static Type? TryResolveUserControl(string fullName)
    {
        if (!string.Equals(fullName, TestUserControlFullName, StringComparison.Ordinal))
            return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, TestUserControlAssemblyName, StringComparison.OrdinalIgnoreCase))
                continue;
            var direct = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (direct is not null)
                return direct;
        }
        return null;
    }

    internal static string MissingClosureMessage(string fullName) =>
        MissingClosureMessage(fullName, DockingManagerAssemblyName);
    internal static string MissingClosureMessage(string fullName, string assemblyName) =>
        $"cannot instantiate custom CLR type '{fullName}' (the converted '{assemblyName}' joint-closure assembly is not loaded; boot the joint package before its BAML)";

    /// <summary>Constructs the real converted instance after verifying
    /// engine-compatible ancestry, concreteness and a parameterless
    /// constructor. Returns false with a precise missing-capability reason
    /// instead of substituting another control or swallowing the failure.</summary>
    internal static bool TryCreate(Type type, out object? instance, out string? failure)
    {
        instance = null;
        if (type.IsAbstract)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (abstract type)";
            return false;
        }
        if (!typeof(UIElement).IsAssignableFrom(type))
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (not an engine UIElement descendant; no substitute panel is created)";
            return false;
        }
        if (type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null) is null)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (needs a parameterless constructor)";
            return false;
        }
        try
        {
            instance = Activator.CreateInstance(type, nonPublic: true)!;
        }
        catch (Exception ex)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (constructor failed: {ex.GetBaseException().Message})";
            return false;
        }
        if (instance is not UIElement)
        {
            instance = null;
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (constructed value is not a visual)";
            return false;
        }
        failure = null;
        return true;
    }

    /// <summary>A hosted converted instance: the exact joint-closure member
    /// (assembly "AvalonDock") carrying engine UIElement ancestry. No type
    /// scan: the caller hands the already-created element.</summary>
    internal static bool IsHostedInstance(object element) =>
        element is UIElement &&
        string.Equals(element.GetType().Assembly.GetName().Name, DockingManagerAssemblyName, StringComparison.OrdinalIgnoreCase);

    // MS-BAML converter ids reused from the reader verified table
    // (BamlReader Conv* rows: BooleanConverter, StringConverter,
    // EnumConverter, DoubleConverter, LengthConverter,
    // ThicknessConverter, CornerRadiusConverter).
    private const short ConvBool = -46;
    private const short ConvString = -615;
    private const short ConvEnum = -195;
    private const short ConvDouble = -175;
    private const short ConvLength = -348;
    private const short ConvThickness = -655;
    private const short ConvCornerRadius = -111;
    private const short ConvGridLength = -256; // GridLengthConverter (same id as BamlReader, verified there by fixture)
    private const short ConvImage = -286; // ImageSourceConverter (same id as BamlReader)

    /// <summary>Applies one per-file BAML attribute to the real hosted
    /// converted instance through its own converted DependencyProperty
    /// (plain CLR properties apply through the real converted setter).
    /// Unknown names, mismatched converters and unconvertible values fail
    /// precise; nothing is substituted or swallowed.</summary>
    internal static bool TryApplyConvertedProperty(object element, string name, string value, short conv, out string? failure)
    {
        var type = element.GetType();
        if (element is not DependencyObject host)
        {
            failure = $"BAML attribute '{name}' on <{type.Name}> (hosted converted values need a DependencyObject host)";
            return false;
        }
        var dp = DependencyProperty.FromName(type, name);
        if (dp is null)
        {
            // Plain CLR properties (no DependencyProperty field) apply
            // through the real converted setter, like native XAML.
            // Anything else stays a precise boundary.
            var clr = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (clr is null || !clr.CanWrite || clr.GetSetMethod() is null)
            {
                failure = $"BAML attribute '{name}' on <{type.Name}> names no dependency property or settable CLR property on the converted type";
                return false;
            }
            if (!IsCompatibleConverter(clr.PropertyType, conv))
            {
                failure = $"BAML attribute '{name}' on <{type.Name}> uses converter {conv} for {clr.PropertyType.Name} (converted-DP apply supports BooleanConverter/bool, StringConverter/string, int32, Double/LengthConverter/double, EnumConverter/enums, ThicknessConverter/Thickness and CornerRadiusConverter/CornerRadius only)";
                return false;
            }
            return ApplyConvertedValue(element, clr.PropertyType, name, value, setter: v => clr.SetValue(element, v), out failure);
        }
        if (!IsCompatibleConverter(dp.PropertyType, conv))
        {
            failure = $"BAML attribute '{name}' on <{type.Name}> uses converter {conv} for {dp.PropertyType.Name} (converted-DP apply supports BooleanConverter/bool, StringConverter/string, int32, Double/LengthConverter/double, EnumConverter/enums, ThicknessConverter/Thickness and CornerRadiusConverter/CornerRadius only)";
            return false;
        }
        return ApplyConvertedValue(element, dp.PropertyType, name, value, v => host.SetValue(dp, v), out failure);
    }

    private static bool ApplyConvertedValue(object element, Type propertyType, string name, string value, Action<object?> setter, out string? failure)
    {
        var type = element.GetType();
        object? converted;
        try
        {
            converted = StyleValueConverter.Convert(value, propertyType);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            failure = $"'{value}' is not a {propertyType.Name} for '{name}' on <{type.Name}> ({ex.Message})";
            return false;
        }
        try
        {
            setter(converted);
        }
        catch (Exception ex)
        {
            failure = $"BAML attribute '{name}' on <{type.Name}> failed to apply ({ex.Message})";
            return false;
        }
        failure = null;
        return true;
    }

    private static bool IsCompatibleConverter(Type propertyType, short conv)
    {
        if (propertyType == typeof(bool)) return conv == ConvBool;
        if (propertyType == typeof(string)) return conv == ConvString;
        // int32 values are self-validating (Selector.SelectedIndex precedent).
        if (propertyType == typeof(int)) return true;
        if (propertyType == typeof(double)) return conv == ConvDouble || conv == ConvLength;
        if (propertyType.IsEnum) return conv == ConvEnum;
        if (propertyType == typeof(Thickness)) return conv == ConvThickness;
        if (propertyType == typeof(CornerRadius)) return conv == ConvCornerRadius;
        if (propertyType == typeof(GridLength)) return conv == ConvGridLength;
        if (typeof(ImageSource).IsAssignableFrom(propertyType)) return conv == ConvImage;
        return false;
    }

    /// <summary>Exact converted-value gate: any public contract type in the
    /// declared joint-closure assembly. The BAML names the exact full name
    /// plus the exact assembly record; nothing else is probed.</summary>
    /// <summary>Converted layout-model gate: a converted-closure instance
    /// that is NOT a UIElement (layout models are non-visual objects, some
    /// rooted at DependencyObject by the converter). Engine and foreign
    /// types never match, and converted UIElements keep the converted-DP
    /// route, so every existing path keeps its exact behavior.</summary>
    internal static bool IsConvertedModelInstance(object element)
    {
        if (element is null || element is UIElement) return false;
        var asm = element.GetType().Assembly.GetName().Name ?? string.Empty;
        return IsClosureAssembly(asm);
    }

    /// <summary>Attaches a parsed child to a converted model object through
    /// the real CLR setter (resolved by the per-file member name). Writable
    /// instance property with a type-compatible child sets; null sets only
    /// reference (or nullable) targets; anything else fails precise.
    /// Mirrors the converted-DP attach for UIElement hosts.</summary>
    internal static bool TrySetConvertedModelMember(object parent, string name, object? child, out string? failure)
    {
        failure = null;
        // Converted layout types are rooted at DependencyObject: when the
        // member is a real dependency property the converted-DP route runs
        // (change notification and coercion intact); otherwise the plain
        // CLR setter below applies.
        if (parent is DependencyObject && DependencyProperty.FromName(parent.GetType(), name) is not null)
            return TryApplyConvertedValue(parent, name, child, out failure);
        var type = parent.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            failure = $"BAML member '{name}' on <{type.Name}> names no CLR property on the converted model object";
            return false;
        }
        if (!property.CanWrite || property.GetSetMethod() is null)
        {
            // Read-only collection property (LayoutPanel.Children et al):
            // WPF content pattern adds the child instead of setting.
            if (property.CanRead && typeof(System.Collections.IList).IsAssignableFrom(property.PropertyType))
            {
                object? list;
                try { list = property.GetValue(parent); }
                catch (Exception ex) { failure = $"BAML member '{name}' on <{type.Name}> cannot read the collection ({ex.GetBaseException().Message})"; return false; }
                if (list is System.Collections.IList items && child is not null)
                {
                    try { items.Add(child); return true; }
                    catch (Exception ex) { failure = $"BAML member '{name}' on <{type.Name}> rejected {child.GetType().FullName} ({ex.GetBaseException().Message})"; return false; }
                }
            }
            failure = $"BAML member '{name}' on <{type.Name}> names no settable CLR property on the converted model object";
            return false;
        }
        if (child is null)
        {
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                failure = $"BAML member '{name}' on <{type.Name}> cannot take null for {property.PropertyType.Name}";
                return false;
            }
            try { property.SetValue(parent, null); }
            catch (Exception ex) { failure = $"BAML member '{name}' on <{type.Name}> failed to apply ({ex.GetBaseException().Message})"; return false; }
            return true;
        }
        if (!property.PropertyType.IsInstanceOfType(child))
        {
            failure = $"BAML member '{name}' on <{type.Name}> needs {property.PropertyType.Name} (have {child.GetType().FullName})";
            return false;
        }
        try { property.SetValue(parent, child); }
        catch (Exception ex) { failure = $"BAML member '{name}' on <{type.Name}> failed to apply ({ex.GetBaseException().Message})"; return false; }
        return true;
    }

    /// <summary>Applies a BAML text value to a converted model object through
    /// the real CLR setter, converting with the shared converter set.
    /// Mirrors TryApplyConvertedProperty minus its DependencyObject
    /// requirement (layout models are plain CLR objects).</summary>
    internal static bool TrySetConvertedModelText(object element, string name, string value, short conv, out string? failure)
    {
        failure = null;
        // Same DP-first ordering as the member route: a real dependency
        // property runs the converted-DP machinery, otherwise the CLR
        // setter below applies with the shared converter set.
        if (element is DependencyObject && DependencyProperty.FromName(element.GetType(), name) is not null)
            return TryApplyConvertedProperty(element, name, value, conv, out failure);
        var type = element.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite || property.GetSetMethod() is null)
        {
            failure = $"BAML attribute '{name}' on <{type.Name}> names no settable CLR property on the converted model object";
            return false;
        }
        // Object-typed converted-model property (verified: LayoutAnchorable
        // ToolTip at the real BAML+0x189E, StringConverter string value):
        // the value converts through the shared set for its own kind and
        // boxes in. Granted scope is string values via StringConverter
        // only; anything else stays a precise boundary. DP-first ordering
        // above is untouched.
        if (property.PropertyType == typeof(object))
        {
            if (conv != ConvString)
            {
                failure = $"BAML attribute '{name}' on <{type.Name}> uses converter {conv} for Object (converted-model object-text takes StringConverter/string values only)";
                return false;
            }
            return ApplyConvertedValue(element, typeof(string), name, value, v => property.SetValue(element, v), out failure);
        }
        if (!IsCompatibleConverter(property.PropertyType, conv))
        {
            failure = $"BAML attribute '{name}' on <{type.Name}> uses converter {conv} for {property.PropertyType.Name} (converted-model apply supports BooleanConverter/bool, StringConverter/string, int32, Double/LengthConverter/double, EnumConverter/enums, ThicknessConverter/Thickness, CornerRadiusConverter/CornerRadius, GridLengthConverter/GridLength and ImageSourceConverter/ImageSource only)";
            return false;
        }
        return ApplyConvertedValue(element, property.PropertyType, name, value, v => property.SetValue(element, v), out failure);
    }

    internal static bool IsHostedValueElement(string typeName, string assemblyRecord)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        return IsClosureAssembly(SimpleAssemblyName(assemblyRecord));
    }
    /// <summary>Exact joint-interface gate for the fork-backed
    /// WindowsFormsHost bridge (HOST-CALL-1425): the BAML assembly
    /// record's simple name must be the joint interface and the type
    /// name the canonical host — same exactness the resolver applies,
    /// never a substring or fallback scan.</summary>
    internal static bool IsBridgeHostElement(string typeName, string assemblyRecord)
    {
        if (!string.Equals(typeName, WindowsFormsHostActivation.HostFullName, StringComparison.Ordinal)) return false;
        return string.Equals(SimpleAssemblyName(assemblyRecord), WindowsFormsHostActivation.AssemblySimpleName, StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Simple assembly name of a BAML assembly record ("Name, Version=..., Culture=..., PublicKeyToken=...").</summary>
    internal static string SimpleAssemblyName(string assemblyRecord) =>
        assemblyRecord.Split(",")[0].Trim();
    /// <summary>The declared converted closure: the AvalonDock engine assembly plus its
    /// converted companion assemblies (themes, serializers). The BAML record names which member;
    /// only an already-loaded member resolves, never a scan.</summary>
    internal static bool IsClosureAssembly(string simpleName) =>
        string.Equals(simpleName, DockingManagerAssemblyName, StringComparison.OrdinalIgnoreCase)
        || simpleName.StartsWith(DockingManagerAssemblyName + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>Instantiates a converted value (theme objects, layout parts)
    /// from the already-loaded joint-closure assembly: exact name, public
    /// contract type, concrete, default constructor, never a Window (runtime
    /// chrome is never a BAML value). UIElement ancestry is NOT required:
    /// values attach through converted DPs, not panel children. Anything
    /// else fails precise; unknown names name the missing closure.</summary>
    internal static bool TryCreateValue(string fullName, string assemblyRecord, out object? value, out string? failure)
    {
        value = null;
        if (!IsHostedValueElement(fullName, assemblyRecord))
        {
            failure = null;
            return false;
        }
        var wanted = SimpleAssemblyName(assemblyRecord);
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
        {
            failure = MissingClosureMessage(fullName, wanted);
            return false;
        }
        Type? type;
        try { type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false); }
        catch (Exception ex)
        {
            failure = $"cannot instantiate custom CLR type '{fullName}' (type lookup failed: {ex.GetBaseException().Message})";
            return false;
        }
        if (type is null)
        {
            failure = $"cannot instantiate custom CLR type '{fullName}' (no loadable converted type with that full name in the joint '{wanted}' closure)";
            return false;
        }
        if (!type.IsPublic && !type.IsNestedPublic)
        {
            failure = $"cannot instantiate custom CLR type '{fullName}' (not a public contract type)";
            return false;
        }
        if (type.IsAbstract)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (abstract type)";
            return false;
        }
        if (typeof(Window).IsAssignableFrom(type))
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (windows are runtime chrome, never BAML values)";
            return false;
        }
        if (type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null) is null)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (needs a parameterless constructor)";
            return false;
        }
        try
        {
            value = Activator.CreateInstance(type, nonPublic: true)!;
        }
        catch (Exception ex)
        {
            failure = $"cannot instantiate custom CLR type '{type.FullName}' (constructor failed: {ex.GetBaseException().Message})";
            return false;
        }
        failure = null;
        return true;
    }

    /// <summary>Applies an already-constructed converted value to a converted
    /// complex property on the hosted parent: the DP resolves by name on the
    /// converted parent type and must accept the value runtime type. Anything
    /// else fails precise; nothing is substituted.</summary>
    internal static bool TryApplyConvertedValue(object parent, string name, object? child, out string? failure)
    {
        var type = parent.GetType();
        if (parent is not DependencyObject host)
        {
            failure = $"complex property '{name}' on <{type.Name}> (hosted converted values need a DependencyObject host)";
            return false;
        }
        var dp = DependencyProperty.FromName(type, name);
        if (dp is null)
        {
            failure = $"complex property '{name}' on <{type.Name}> names no dependency property on the converted type";
            return false;
        }
        if (child is null || !dp.PropertyType.IsInstanceOfType(child))
        {
            failure = $"complex property '{name}' on <{type.Name}> needs {dp.PropertyType.FullName} (have {(child is null ? "null" : child.GetType().FullName)})";
            return false;
        }
        try
        {
            host.SetValue(dp, child);
        }
        catch (Exception ex)
        {
            failure = $"complex property '{name}' on <{type.Name}> failed to apply ({ex.Message})";
            return false;
        }
        failure = null;
        return true;
    }
}


