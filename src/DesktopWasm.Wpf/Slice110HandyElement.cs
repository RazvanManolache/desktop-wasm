// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

namespace DesktopWasm.Wpf;

// Slice-110 HandyControl converted-target style fallback (Gravatar; slice-112
// adds NumericUpDownExtend the same way).
//
// The staged GravatarDemo view reads GravatarCircleImg (line 14) and
// GravatarCircle (lines 18, 20) at application scope. Upstream both are lib
// Theme.xaml styles with TargetType hc:Gravatar chaining through BAML-only
// base styles (GravatarCircle BasedOn GravatarBaseStyle with a Padding plus
// BorderCircularClip-template setter; GravatarCircleImg BasedOn
// GravatarCircle with Padding 0), so both land as empty chrome-less shims
// with the correct TargetType (the slice-100 idiom; no setter content is
// replicated and styled rows keep default control chrome).
//
// The styles resolve in code, not XAML (the Slice105 converter precedent):
// resource entries cannot name converted lib types without a staged image
// present, and a converted TargetType inside HandyTheme.Xaml would turn every
// unrelated miss into a parse failure. The Gravatar type is resolved from
// the converted images (CustomElementSource first, then the loaded-image
// scan, single UIElement-derived match wins); misses stay false like WPF.
// Cached per source assembly, so repeat lookups share one Style instance
// (sealed on apply, like real shared styles).
internal static class Slice110HandyElement
{
    private static readonly object StyleGate = new();
    private static Assembly? _styleSource;
    private static readonly Dictionary<string, Style> StyleCache = new(StringComparer.Ordinal);

    /// <summary>Converted-target style keys (style key to converted control full name).</summary>
    private static readonly Dictionary<string, string> ConvertedStyleTargets = new(StringComparer.Ordinal)
    {
        ["GravatarCircle"] = "HandyControl.Controls.Gravatar",
        ["GravatarCircleImg"] = "HandyControl.Controls.Gravatar",
        ["NumericUpDownExtend"] = "HandyControl.Controls.NumericUpDown",
    };

    /// <summary>
    /// Resolves a converted-target style by resource key. True with the
    /// shared empty Style (TargetType the converted control) when the
    /// staged images carry that exact type; false (a WPF miss) otherwise.
    /// </summary>
    internal static bool TryResolveConvertedStyle(object key, out object? value)
    {
        value = null;
        if (key is not string name || !ConvertedStyleTargets.TryGetValue(name, out var targetName))
            return false;
        var source = XamlLoader.CustomElementSource;
        if (source is null)
            return false;
        lock (StyleGate)
        {
            if (!ReferenceEquals(_styleSource, source))
            {
                _styleSource = source;
                StyleCache.Clear();
            }
            if (StyleCache.TryGetValue(name, out var cached))
            {
                value = cached;
                return true;
            }
            var target = FindConvertedType(source, targetName);
            if (target is null)
                return false;
            var style = new Style(target);
            StyleCache[name] = style;
            value = style;
            return true;
        }
    }

    private static Type? FindConvertedType(Assembly source, string fullName)
    {
        Type? found = null;
        var engine = typeof(UIElement).Assembly;
        var assemblies = new List<Assembly> { source };
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, engine) || assemblies.Contains(assembly))
                continue;
            assemblies.Add(assembly);
        }
        foreach (var assembly in assemblies)
        {
            Type? candidate;
            try { candidate = assembly.GetType(fullName, throwOnError: false, ignoreCase: false); }
            catch { continue; }
            if (candidate is null || !typeof(UIElement).IsAssignableFrom(candidate))
                continue;
            if (found is not null && !ReferenceEquals(found, candidate))
                return null;
            found = candidate;
        }
        return found;
    }
}
