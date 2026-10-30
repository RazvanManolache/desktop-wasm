// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Xml.Linq;

namespace DesktopWasm.Wpf;

// Slice-107 HandyControl converted-Card default template.
//
// The staged CardDemo view hosts converted hc:Card controls (ContentControl
// subclass from the staged HandyControl image) with Footer/FooterTemplate
// bindings but no Style or Template: the lib default style (CardBaseStyle,
// Themes/Styles/Base/CardBaseStyle.xaml, MIT) ships BAML-only behind the
// line-17 SkinDefault stop in App.xaml, so the engine theme path
// (Themes/Generic.xaml by DefaultStyleKey) stays a no-op for this assembly
// and cards measured zero with no paint.
//
// The engine therefore carries the upstream template structure as its
// converted-default template for Card-shaped controls: a Border (templated
// BorderBrush/BorderThickness/Background) over a vertical header/content/
// footer stack, each header/footer slot collapsing through the upstream
// Object2VisibilityConverter semantics with a ContentPresenter mirroring the
// matching property plus its template/selector/format. Corner radius is the
// documented subset: the upstream root binds the converted
// BorderElement.CornerRadius attached value, which stays unreachable behind
// the BAML stop, so the default root keeps square corners. Applied only to
// converted ContentControl subclasses exposing the exact Card DP shape
// (object Header/Footer, DataTemplate HeaderTemplate/FooterTemplate) with no
// explicit Style or Template; engine types and explicitly styled controls
// never match, and misses still fail like WPF.
internal static class Slice107HandyCard
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, bool> ShapeCache = new();
    private static readonly Dictionary<Type, ControlTemplate> TemplateCache = new();

    /// <summary>
    /// Default template markup (mirrors the upstream CardBaseStyle visual
    /// structure with engine-parseable constructs only: TemplateBinding plus
    /// the Object2VisibilityConverter application key, ContentSource
    /// mirroring, no attached-property bindings).
    /// </summary>
    internal const string TemplateXaml =
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"> " +
        "<Border BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" Background=\"{TemplateBinding Background}\"> " +
        "<StackPanel> " +
        "<Border BorderThickness=\"0,0,0,1\" BorderBrush=\"{TemplateBinding BorderBrush}\" Visibility=\"{TemplateBinding Header, Converter={StaticResource Object2VisibilityConverter}}\"> " +
        "<ContentPresenter ContentSource=\"Header\" ContentTemplate=\"{TemplateBinding HeaderTemplate}\" ContentTemplateSelector=\"{TemplateBinding HeaderTemplateSelector}\" ContentStringFormat=\"{TemplateBinding HeaderStringFormat}\" /> " +
        "</Border> " +
        "<ContentPresenter /> " +
        "<Border BorderThickness=\"0,1,0,0\" BorderBrush=\"{TemplateBinding BorderBrush}\" Visibility=\"{TemplateBinding Footer, Converter={StaticResource Object2VisibilityConverter}}\"> " +
        "<ContentPresenter ContentSource=\"Footer\" ContentTemplate=\"{TemplateBinding FooterTemplate}\" ContentTemplateSelector=\"{TemplateBinding FooterTemplateSelector}\" ContentStringFormat=\"{TemplateBinding FooterStringFormat}\" /> " +
        "</Border> " +
        "</StackPanel> " +
        "</Border> " +
        "</ControlTemplate>";

    /// <summary>
    /// Whether the element is a converted Card-shaped control (converted
    /// assembly, ContentControl lineage, exact eight-slot Card DP shape).
    /// Cached per type; engine types never match.
    /// </summary>
    internal static bool IsCardShape(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var type = element.GetType();
        lock (Gate)
        {
            if (ShapeCache.TryGetValue(type, out var hit)) return hit;
        }
        var shaped = ComputeShape(type);
        lock (Gate) { ShapeCache[type] = shaped; }
        return shaped;
    }

    private static bool ComputeShape(Type type)
    {
        if (ReferenceEquals(type.Assembly, typeof(ContentControl).Assembly)) return false;
        if (!typeof(ContentControl).IsAssignableFrom(type)) return false;
        // The default template binds all eight slots (value, template,
        // selector, string format per side), so a partial shape stays out
        // rather than failing template refresh mid-load.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        var header = type.GetProperty("Header", flags);
        var footer = type.GetProperty("Footer", flags);
        var headerTemplate = type.GetProperty("HeaderTemplate", flags);
        var headerSelector = type.GetProperty("HeaderTemplateSelector", flags);
        var headerFormat = type.GetProperty("HeaderStringFormat", flags);
        var footerTemplate = type.GetProperty("FooterTemplate", flags);
        var footerSelector = type.GetProperty("FooterTemplateSelector", flags);
        var footerFormat = type.GetProperty("FooterStringFormat", flags);
        if (header is null || footer is null || headerTemplate is null || headerSelector is null ||
            headerFormat is null || footerTemplate is null || footerSelector is null || footerFormat is null)
            return false;
        if (header.PropertyType != typeof(object) || footer.PropertyType != typeof(object)) return false;
        if (headerTemplate.PropertyType != typeof(DataTemplate) || footerTemplate.PropertyType != typeof(DataTemplate))
            return false;
        if (headerSelector.PropertyType != typeof(DataTemplateSelector) || footerSelector.PropertyType != typeof(DataTemplateSelector))
            return false;
        if (headerFormat.PropertyType != typeof(string) || footerFormat.PropertyType != typeof(string))
            return false;
        return true;
    }

    /// <summary>
    /// Applies the shared converted-default template when the control is a
    /// Card-shaped converted control with no explicit Style or Template.
    /// True when the template was installed (or was already present from a
    /// previous pass); false leaves the element untouched.
    /// </summary>
    internal static bool TryApplyCardDefault(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.Template is not null || control.Style is not null) return false;
        if (!IsCardShape(control)) return false;
        control.Template = GetSharedTemplate(control.GetType());
        return control.Template is not null;
    }

    /// <summary>Shared default template per Card-shaped type (fresh tree per control at apply).</summary>
    internal static ControlTemplate GetSharedTemplate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (Gate)
        {
            if (TemplateCache.TryGetValue(type, out var hit)) return hit;
        }
        var node = XElement.Parse(TemplateXaml);
        var template = XamlLoader.ReadControlTemplate(node, type, XamlLoader.NoLineInfo);
        lock (Gate) { TemplateCache[type] = template; }
        return template;
    }
}
