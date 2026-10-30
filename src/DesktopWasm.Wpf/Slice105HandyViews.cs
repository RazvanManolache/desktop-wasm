// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

namespace DesktopWasm.Wpf;

// Slice-105 HandyControl converted-converter fallback.
//
// The staged BorderDemo view reads StringRepeatConverter (its ex:Lang
// Converter on the BorderTip rows, line 45) and the staged ChatBox and
// NotifyIconDemo views read Boolean2VisibilityReConverter (their Visibility
// bindings) at application scope. The demo registers both in loose XAML:
// Resources/Themes/Basic/Converters.xaml (staged in the entry image) carries
// <converter:StringRepeatConverter x:Key="StringRepeatConverter"/> plus the
// sibling converters, and the lib key reverses the stock visibility mapping.
// The keys live behind the line-17 SkinDefault BAML-only stop with the rest
// of App.xaml, so neither resolves from converted resources today.
//
// The engine therefore instantiates the real converted types, like the
// slice-104 Locator: "StringRepeatConverter" news the converted
// HandyControlDemo.Tools.Converter.StringRepeatConverter from the staged
// entry (default constructor, IValueConverter, verified against the staged
// images), and "Boolean2VisibilityReConverter" news the converted
// HandyControl.Tools.Converter.Boolean2VisibilityReConverter from the staged
// lib. Cached per source assembly, misses stay false like WPF. Resource
// entries stay out of it: a CLR-namespace entry would throw without a staged
// image present and turn every unrelated miss into a parse failure.
internal static class Slice105HandyViews
{
    private static readonly object ConverterGate = new();
    private static Assembly? _converterSource;
    private static readonly Dictionary<string, object> ConverterCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a converted demo/lib value converter by resource key. True
    /// with the shared converted instance when the current element source
    /// carries that exact type with a default constructor implementing
    /// IValueConverter; false (a WPF miss) otherwise.
    /// </summary>
    internal static bool TryResolveConvertedConverter(object key, out object? value)
    {
        value = null;
        if (key is not string name)
            return false;
        var fullName = name switch
        {
            "StringRepeatConverter" => "HandyControlDemo.Tools.Converter.StringRepeatConverter",
            "Boolean2VisibilityReConverter" => "HandyControl.Tools.Converter.Boolean2VisibilityReConverter",
            _ => null,
        };
        if (fullName is null)
            return false;
        var source = XamlLoader.CustomElementSource;
        if (source is null)
            return false;
        lock (ConverterGate)
        {
            if (!ReferenceEquals(_converterSource, source))
            {
                _converterSource = source;
                ConverterCache.Clear();
            }
            if (ConverterCache.TryGetValue(name, out value))
                return value is not null;
            Type? type = null;
            foreach (var assembly in ConvertedAssemblies(source))
            {
                Type? candidate;
                try { candidate = assembly.GetType(fullName, throwOnError: false, ignoreCase: false); }
                catch { continue; }
                if (candidate is null)
                    continue;
                type = candidate;
                break;
            }
            if (type is null || type.IsAbstract || !typeof(IValueConverter).IsAssignableFrom(type))
                return false;
            if (type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null) is null)
                return false;
            object instance;
            try { instance = Activator.CreateInstance(type, nonPublic: true)!; }
            catch { return false; }
            ConverterCache[name] = instance;
            value = instance;
            return true;
        }
    }

    private static List<Assembly> ConvertedAssemblies(Assembly source)
    {
        var assemblies = new List<Assembly> { source };
        var engine = typeof(UIElement).Assembly;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, engine) || assemblies.Contains(assembly))
                continue;
            assemblies.Add(assembly);
        }
        return assemblies;
    }
}
