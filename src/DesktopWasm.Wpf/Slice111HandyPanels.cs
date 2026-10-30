// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

namespace DesktopWasm.Wpf;

// Slice-111 HandyControl converted panel layouts.
//
// Two converted layout surfaces run converted code now, with layout fidelity
// (arranged rects), not just node counts:
//
// (1) Converted HoneycombPanel as a legacy-ListBox rows host. The staged
// HoneycombPanelDemo ListBox names
// <hc:HoneycombPanel hc:PanelElement.FluidMoveBehavior="{StaticResource BehaviorXY200}"/>
// as its ItemsPanel. The probe path inflated-and-discarded it (WrapPanel and
// horizontal-StackPanel have shared engine math; everything else kept the
// legacy vertical stack), so the converted MeasureOverride/ArrangeOverride
// never ran. The legacy ListBox now keeps a converted (non-engine-assembly)
// Panel as a real rows host: rows parent into it and measure/arrange/paint
// route through it, mirroring engine ItemsControl._itemsHost. Engine panels
// keep their exact prior paths (WrapPanel flow, horizontal StackPanel stack,
// everything else legacy) and grouped lists keep the grouped path: only a
// converted panel on a flat list engages the host.
//
// The template inflate needs two previously-missing pieces, both retained and
// animation-inert (the Blend FluidMove animation stays a declared boundary,
// like the slice-100 FluidMoveBehaviorWrapPanelItemsPanelTemplate precedent
// which lands a plain WrapPanel and the TransitionStoryboard precedent):
// engine PanelElement with an object-typed FluidMoveBehavior attached DP
// (upstream HandyControl.Controls.PanelElement declares the DP over the
// Blend FluidMoveBehavior type, which never stages), and BehaviorXY200 as a
// shared inert marker resolved in code at application scope (the BAML-only
// lib Themes/Basic/Behaviors.xaml record it names never ships; resource
// entries cannot name converted lib types without a staged image present,
// the slice-105 converter precedent). The marker is retained on the panel
// and never animated.
//
// (2) Converted ElementGroup/ButtonGroup default items panels. Both are
// converted ItemsControl subclasses with Orientation/Layout DPs but no
// layout overrides of their own: real layout comes from their default-style
// ItemsPanel mapping (upstream lib Themes/Theme.xaml ElementGroupBaseStyle +
// ButtonGroupBaseStyle, verified in-workspace):
// Uniform+Horizontal -> UniformGrid Rows=1, Uniform+Vertical -> UniformGrid
// Columns=1, Stack+Horizontal -> StackPanel Horizontal, Stack+Vertical ->
// StackPanel. The theme ships BAML-only behind the line-17 SkinDefault stop,
// so the engine carries the mapping: when ItemsPanel is unset, the getter
// falls back to the cached template matching the live Orientation/Layout
// (explicit <X.ItemsPanel> still wins, mirroring local-beats-style). Runtime
// Orientation/Layout edits re-read on the next Regenerate/measure because
// the fallback evaluates live on every get, so no subscription machinery is
// needed; the triggers' re-apply is exactly this read.
//
// (3) Converted paint-less OnRender backstop. Upstream ElementGroup.OnRender
// only refreshes corner radii and ButtonGroup.OnRender only re-selects item
// styles: neither paints children (in real WPF children render through the
// visual tree independent of the parent's OnRender). The engine paints
// children from the parent's OnRender, so converted groups rendered nothing.
// RenderVisible now replays the ItemsControl child walk for exactly these
// two converted types after their OnRender returns. Converted types calling
// base.OnRender (StepBar) already paint through the base and stay single;
// engine types keep their own choreography untouched.
//
// Boundaries, never fabricated: Blend FluidMove animation (no behavior
// attached, marker inert); engine (non-converted) panels in legacy ListBox
// ItemsPanel keep the legacy stack (WrapPanel/horizontal-StackPanel keep
// their shared math); grouped legacy lists ignore the custom host; margin
// folding inside converted arrange keeps the engine slot semantic
// (RenderRect is the slot as passed, like every converted arrange caller);
// runtime Orientation/Layout edits apply on the next layout pass, not
// through style triggers; the 2 absent binaries (no browser path) and the
// line-17 SkinDefault BAML-only stop are unchanged.
internal static class Slice111HandyPanels
{
    private const string ElementGroupName = "HandyControl.Controls.ElementGroup";
    private const string ButtonGroupName = "HandyControl.Controls.ButtonGroup";

    /// <summary>Converted group-panel full names carrying the Orientation/Layout mapping.</summary>
    internal static bool IsConvertedGroupPanel(Type type) =>
        type.FullName is ElementGroupName or ButtonGroupName;

    /// <summary>
    /// Default items panel for converted ElementGroup/ButtonGroup when no
    /// explicit ItemsPanel is set (null otherwise, including all engine
    /// types): the upstream style mapping evaluated against the live
    /// Orientation/Layout values. Explicit panels always win because the
    /// caller only consults this when its own slot is null.
    /// </summary>
    internal static ItemsPanelTemplate? DefaultGroupItemsPanel(ItemsControl owner)
    {
        if (!IsConvertedGroupPanel(owner.GetType()))
            return null;
        var orientation = ReadConvertedEnum(owner, "Orientation", "Horizontal");
        var layout = ReadConvertedEnum(owner, "Layout", "Uniform");
        var horizontal = string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
        var stack = string.Equals(layout, "Stack", StringComparison.OrdinalIgnoreCase);
        return (stack, horizontal) switch
        {
            (true, true) => StackHorizontalPanel,
            (true, false) => StackVerticalPanel,
            (false, true) => UniformRowsPanel,
            _ => UniformColumnsPanel,
        };
    }

    private static string ReadConvertedEnum(ItemsControl owner, string property, string fallback)
    {
        try
        {
            var value = owner.GetType().GetProperty(
                property,
                BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
            var text = value?.ToString();
            if (!string.IsNullOrEmpty(text))
                return text;
        }
        catch
        {
        }
        return fallback;
    }

    private static readonly ItemsPanelTemplate StackHorizontalPanel =
        new("<StackPanel Orientation=\"Horizontal\" />");

    private static readonly ItemsPanelTemplate StackVerticalPanel =
        new("<StackPanel />");

    private static readonly ItemsPanelTemplate UniformRowsPanel =
        new("<UniformGrid Rows=\"1\" />");

    private static readonly ItemsPanelTemplate UniformColumnsPanel =
        new("<UniformGrid Columns=\"1\" />");

    /// <summary>
    /// Maps an (orientation, layout) pair to the upstream style panel, the
    /// pure core of <see cref="DefaultGroupItemsPanel"/> (engine-testable
    /// without converted images).
    /// </summary>
    internal static string MapGroupPanelKind(string orientation, string layout)
    {
        var horizontal = string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);
        var stack = string.Equals(layout, "Stack", StringComparison.OrdinalIgnoreCase);
        return (stack, horizontal) switch
        {
            (true, true) => "StackHorizontal",
            (true, false) => "StackVertical",
            (false, true) => "UniformRows",
            _ => "UniformColumns",
        };
    }

    /// <summary>
    /// The shared inert FluidMoveBehavior marker behind key BehaviorXY200
    /// (the BAML-only Behaviors.xaml record the staged HoneycombPanelDemo
    /// template names): retained on PanelElement.FluidMoveBehavior, never
    /// animated, never attached to the interactivity system.
    /// </summary>
    internal static readonly object BehaviorXY200Marker = new();

    /// <summary>
    /// Resolves BehaviorXY200 in code (true with the shared inert marker;
    /// false for any other key, so misses stay false like WPF).
    /// </summary>
    internal static bool TryResolveBehaviorXY200(object key, out object? value)
    {
        value = null;
        if (key is not string name || !string.Equals(name, "BehaviorXY200", StringComparison.Ordinal))
            return false;
        value = BehaviorXY200Marker;
        return true;
    }
}

/// <summary>
/// Attach owner for the HandyControl Blend layout knob (matches
/// HandyControl.Controls.PanelElement.FluidMoveBehaviorProperty): retained
/// only, no animation backend in the session, so sets land and read back
/// without effect, the honest-hint treatment (VirtualizingStackPanel and
/// ScrollViewer.IsInertiaEnabled precedent). The converted owner stays
/// load-resolvable for converted member refs; engine XAML attribution wins
/// by the engine-first rule, so converted callbacks never fire here.
/// </summary>
public static class PanelElement
{
    /// <summary>Retained layout-animation slot (object-typed: the Blend type never stages).</summary>
    public static readonly DependencyProperty FluidMoveBehaviorProperty =
        DependencyProperty.RegisterAttached("FluidMoveBehavior", typeof(object), typeof(PanelElement),
            new PropertyMetadata(null));

    /// <summary>Retains the behavior marker (matches SetFluidMoveBehavior).</summary>
    public static void SetFluidMoveBehavior(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FluidMoveBehaviorProperty, value);
    }

    /// <summary>Reads the retained marker (matches GetFluidMoveBehavior).</summary>
    public static object? GetFluidMoveBehavior(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FluidMoveBehaviorProperty);
    }
}
