// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml;

namespace DesktopWasm.Wpf;

// Slice-102 HandyControl converted-element precedence.
//
// The converted HandyControlDemo views (MainContent line 16 <hc:Watermark>,
// line 20 <hc:Drawer>; LeftMainContent ContentTemplate <hc:ElementGroup>,
// <hc:SearchBar>, <hc:HighlightTextBlock>) name HandyControl.Controls types
// that exist in the staged converted HandyControl lib image with real
// behavior (Watermark's DP surface, Drawer's adorner open/close, SearchBar's
// Enter-to-SearchStarted plus ICommandSource, HighlightTextBlock's
// SourceText/QueriesText highlighting). The engine carries no copies: like
// the slice-49 converted PlotView hosts and the slice-99 converted
// interactivity surface, the loader drives the converted types on the
// engine bases (System.Windows.Controls.* refs in the converted images
// already map to the engine via the default Convert rule), so converted
// DPs, CLR properties, bindings and routed events keep their upstream
// semantics instead of a re-implementation drifting from them.
//
// Member evidence via the staged images themselves (hash-verified, probed
// in-process): HandyControl.Controls.Watermark/Drawer/SearchBar/
// HighlightTextBlock/CircleProgressBar/GifImage instantiate cleanly with
// default constructors and are UIElement-derived with exactly one match per
// short name across the converted closure; ElementGroup's static
// initializer reads StackPanel.OrientationProperty, which the engine now
// declares (real WPF parity: a plain, non-attached DP defaulting to
// Vertical, CLR wrapper re-routed store-backed like the slice-62 alignment
// DPs). A resolved converted type whose constructor throws surfaces the
// real contract gap instead of silently falling back; zero or ambiguous
// matches keep the precise Unknown-element error.
//
// Precedence, never a silent swap: engine-native names in the presentation
// namespace keep winning (SimplePanel/TransitioningContentControl stay
// engine-owned like before), and CustomElementSource short names win over
// the lib scan (converted apps keep one control per short name, entry-local
// first). Only the HandyControl xmlns table
// (https://handyorg.github.io/handycontrol, the verified copy in
// Slice97LangMarkup) routes here, so unrelated XAML is untouched. Converted
// templates/styles stay precise boundaries: unstyled converted controls
// render their engine-base chrome only (the lib theme ships BAML-only,
// behind the line-17 SkinDefault stop), which is honest-empty, never
// fabricated pixels.
/// <summary>
/// Stack panel with an orientation knob (matches
/// System.Windows.Controls.VirtualizingStackPanel: Orientation defaults to
/// Vertical like WPF; the attached IsVirtualizingProperty keeps its
/// attached/true-default/inherits contract below). The engine stacks like a
/// StackPanel and realizes every item, so item windowing stays a declared
/// boundary - the same honest treatment as the IsVirtualizing hint the
/// engine ListBox already carries. BAML ItemsPanelTemplate visuals land here
/// (the staged HandyControl
/// 'VirtualizingStackHorizontal/VerticalItemsPanelTemplate' entries); loose
/// XAML resolves the same name through the engine factory.
/// </summary>
public class VirtualizingStackPanel : Panel
{
    /// <summary>Virtualization hint (matches IsVirtualizingProperty).</summary>
    public static readonly DependencyProperty IsVirtualizingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizing", typeof(bool), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Reads the hint (matches GetIsVirtualizing).</summary>
    public static bool GetIsVirtualizing(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool?)element.GetValue(IsVirtualizingProperty) ?? true;
    }

    /// <summary>Writes the hint (matches SetIsVirtualizing).</summary>
    public static void SetIsVirtualizing(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsVirtualizingProperty, value);
    }

    /// <summary>Stack direction (matches OrientationProperty; Vertical default like WPF).</summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register("Orientation", typeof(Orientation), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(Orientation.Vertical) { AffectsMeasure = true });

    /// <summary>Stack direction (store-backed; re-layouts on change).</summary>
    public Orientation Orientation
    {
        get => (Orientation?)GetValue(OrientationProperty) ?? Orientation.Vertical;
        set => SetValue(OrientationProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Orientation == Orientation.Horizontal)
        {
            var w = 0.0; var h = 0.0;
            foreach (UIElement c in Children)
            {
                var m = GetMargin(c);
                c.Measure(new WpfSize(available.Width, Math.Max(0, available.Height - m.Top - m.Bottom)));
                w += c.DesiredSize.Width + m.Left + m.Right;
                h = Math.Max(h, c.DesiredSize.Height + m.Top + m.Bottom);
            }
            return new WpfSize(w, h);
        }
        var vw = 0.0; var vh = 0.0;
        foreach (UIElement c in Children)
        {
            var m = GetMargin(c);
            c.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right), available.Height));
            vw = Math.Max(vw, c.DesiredSize.Width + m.Left + m.Right);
            vh += c.DesiredSize.Height + m.Top + m.Bottom;
        }
        return new WpfSize(vw, vh);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Orientation == Orientation.Horizontal)
        {
            var x = finalRect.X;
            foreach (UIElement c in Children)
            {
                var m = GetMargin(c);
                var va = c is FrameworkElement fe ? fe.VerticalAlignment : VerticalAlignment.Top;
                var slotH = Math.Max(0, finalRect.Height - m.Top - m.Bottom);
                var childH = Math.Min(slotH, c.DesiredSize.Height);
                var offset = va switch
                {
                    VerticalAlignment.Center => (slotH - childH) / 2,
                    VerticalAlignment.Bottom => slotH - childH,
                    _ => 0.0,
                };
                var childW = c.DesiredSize.Width;
                c.Arrange(new WpfRect(x + m.Left, finalRect.Y + m.Top + offset, childW, childH));
                x += m.Left + childW + m.Right;
            }
            return;
        }
        var y = finalRect.Y;
        foreach (UIElement c in Children)
        {
            var m = GetMargin(c);
            var ha = c is FrameworkElement fe ? fe.HorizontalAlignment : HorizontalAlignment.Stretch;
            var slotW = Math.Max(0, finalRect.Width - m.Left - m.Right);
            var childW = ha == HorizontalAlignment.Stretch ? slotW : Math.Min(slotW, c.DesiredSize.Width);
            var offset = ha switch
            {
                HorizontalAlignment.Center => (slotW - childW) / 2,
                HorizontalAlignment.Right => slotW - childW,
                _ => 0.0,
            };
            var x = finalRect.X + m.Left + offset;
            var childH = c.DesiredSize.Height;
            c.Arrange(new WpfRect(x, y + m.Top, childW, childH));
            y += m.Top + childH + m.Bottom;
        }
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);
}

public static partial class XamlLoader
{
    /// <summary>
    /// Resolves a vendor element (hc:, materialDesign:) to its staged converted
    /// type (exactly one UIElement-derived match with a default constructor
    /// across the converted images under the carried xmlns tables).
    /// Returns null when the namespace is not a carried one or no
    /// converted type matches, so the caller keeps its legacy error.
    /// </summary>
    /// <summary>Instantiates a clr-namespace element by exact CLR name
    /// across the loaded converted images (null when the URI is not a
    /// clr-namespace or no image carries the type).</summary>
    internal static UIElement? TryCreateClrNamespaceElement(string name, string namespaceUri, IXmlLineInfo lineInfo)
    {
        var target = ClrNamespaceTarget(namespaceUri, name);
        if (target is null) return null;
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            Type? candidate;
            try { candidate = assembly.GetType(target, throwOnError: false, ignoreCase: false); }
            catch { continue; }
            if (candidate is null || !typeof(UIElement).IsAssignableFrom(candidate)) continue;
            if (candidate.IsAbstract)
                throw Fail(lineInfo, $"<{name}> resolves to abstract converted type '{candidate.FullName}'.");
            if (!HasDefaultCtor(candidate))
                throw Fail(lineInfo, $"<{name}> resolves to converted type '{candidate.FullName}' with no default constructor.");
            return (UIElement)Activator.CreateInstance(candidate, nonPublic: true)!;
        }
        return null;
    }

    internal static UIElement? TryCreateConvertedHandyElement(
        string name, string namespaceUri, IXmlLineInfo lineInfo)
    {
        if (XmlNamespacesFor(namespaceUri) is not { } namespaces)
            return null;
        Type? found = null;
        foreach (var ns in namespaces)
        {
            var fullName = ns + "." + name;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                var candidate = SafeGetConvertedType(assembly, fullName);
                if (candidate is null || !typeof(UIElement).IsAssignableFrom(candidate))
                    continue;
                if (found is not null && !ReferenceEquals(found, candidate))
                    throw Fail(lineInfo,
                        $"<{name}> matches '{found.FullName}' and '{candidate.FullName}' (keep one converted control per short name).");
                found = candidate;
            }
        }
        if (found is null)
            return null;
        if (found.IsAbstract)
            throw Fail(lineInfo, $"<{name}> resolves to abstract converted type '{found.FullName}'.");
        if (!HasDefaultCtor(found))
            throw Fail(lineInfo, $"<{name}> resolves to converted type '{found.FullName}' with no default constructor.");
        return (UIElement)Activator.CreateInstance(found, nonPublic: true)!;
    }
}
