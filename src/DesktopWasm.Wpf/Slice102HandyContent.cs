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
/// <summary>Virtualization mode (matches System.Windows.Controls.VirtualizationMode).</summary>
public enum VirtualizationMode
{
    Standard = 0,
    Recycling = 1,
}

/// <summary>
/// Virtualizing stack panel (matches System.Windows.Controls.VirtualizingStackPanel;
/// Vertical like WPF; the attached IsVirtualizingProperty keeps its
/// attached/true-default/inherits contract below). Realizes item containers on
/// demand within the visible viewport and caches measured bounds for high-performance
/// LargeDataSet items controls.
/// </summary>
public class VirtualizingStackPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>Virtualization hint (matches IsVirtualizingProperty).</summary>
    public static readonly new DependencyProperty IsVirtualizingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizing", typeof(bool), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Reads the hint (matches GetIsVirtualizing).</summary>
    public static new bool GetIsVirtualizing(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("VirtualizingStackPanel.IsVirtualizing") is bool fromBag) return fromBag;
        if (element is UIElement visual2 && visual2.GetAttached("VirtualizingPanel.IsVirtualizing") is bool fromBag2) return fromBag2;
        if (element.HasLocalValue(IsVirtualizingProperty) && element.GetValue(IsVirtualizingProperty) is bool fromStore) return fromStore;
        if (element.HasLocalValue(VirtualizingPanel.IsVirtualizingProperty) && element.GetValue(VirtualizingPanel.IsVirtualizingProperty) is bool fromStore2) return fromStore2;
        return true;
    }

    /// <summary>Writes the hint (matches SetIsVirtualizing).</summary>
    public static new void SetIsVirtualizing(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual) visual.SetAttached("VirtualizingStackPanel.IsVirtualizing", value);
        element.SetValue(IsVirtualizingProperty, value);
        VirtualizingPanel.SetIsVirtualizing(element, value);
    }

    /// <summary>Virtualization mode knob (matches VirtualizationModeProperty).</summary>
    public static readonly new DependencyProperty VirtualizationModeProperty =
        DependencyProperty.RegisterAttached("VirtualizationMode", typeof(VirtualizationMode), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(VirtualizationMode.Standard));

    /// <summary>Reads the virtualization mode (matches GetVirtualizationMode).</summary>
    public static new VirtualizationMode GetVirtualizationMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("VirtualizingStackPanel.VirtualizationMode") is VirtualizationMode fromBag) return fromBag;
        if (element is UIElement visual2 && visual2.GetAttached("VirtualizingPanel.VirtualizationMode") is VirtualizationMode fromBag2) return fromBag2;
        if (element.HasLocalValue(VirtualizationModeProperty) && element.GetValue(VirtualizationModeProperty) is VirtualizationMode fromStore) return fromStore;
        if (element.HasLocalValue(VirtualizingPanel.VirtualizationModeProperty) && element.GetValue(VirtualizingPanel.VirtualizationModeProperty) is VirtualizationMode fromStore2) return fromStore2;
        return VirtualizationMode.Standard;
    }

    /// <summary>Writes the virtualization mode (matches SetVirtualizationMode).</summary>
    public static new void SetVirtualizationMode(DependencyObject element, VirtualizationMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual)
        {
            visual.SetAttached("VirtualizingStackPanel.VirtualizationMode", value);
            visual.SetAttached("VirtualizingPanel.VirtualizationMode", value);
        }
        element.SetValue(VirtualizationModeProperty, value);
    }

    /// <summary>Virtualization mode for container recycling (matches VirtualizingStackPanel.VirtualizationMode).</summary>
    public VirtualizationMode VirtualizationMode
    {
        get => GetVirtualizationMode(this);
        set => SetVirtualizationMode(this, value);
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

    // Bounds cache: maps item index -> measured main dimension (Height if Vertical, Width if Horizontal)
    private readonly Dictionary<int, double> _boundsCache = new();
    private double _estimatedItemDimension = 24.0;

    private (ItemContainerGenerator? Generator, System.Collections.IList? Items, FrameworkElement? OwnerVisual) ResolveOwnerInfo()
    {
        if (ItemsControl.GetItemsOwner(this) is { } ic)
            return (ic.ItemContainerGenerator, ic.Items, ic);
        for (var node = (DependencyObject?)this; node is not null; node = (node as FrameworkElement)?.Parent)
            if (node is ListBox lb)
                return (lb.ItemContainerGenerator, lb.ItemContainerGenerator.ContainerItems, lb);
        return (null, null, null);
    }

    /// <summary>Number of currently realized item containers.</summary>
    public int RealizedCount
    {
        get
        {
            var (generator, _, _) = ResolveOwnerInfo();
            return generator?.RealizedCount ?? Children.Count;
        }
    }

    /// <summary>Cached or estimated dimension for item at index.</summary>
    public double GetItemBounds(int index)
    {
        if (_boundsCache.TryGetValue(index, out var cached)) return cached;
        return _estimatedItemDimension;
    }

    /// <summary>Total extent across all items (sum of cached and estimated bounds).</summary>
    public double GetTotalExtent(int count)
    {
        var total = 0.0;
        for (var i = 0; i < count; i++) total += GetItemBounds(i);
        return total;
    }

    public void OnItemsAdded(int index, int count)
    {
        if (_boundsCache.Count == 0) return;
        var shifted = new List<KeyValuePair<int, double>>();
        foreach (var kvp in _boundsCache.Where(k => k.Key >= index).OrderByDescending(k => k.Key).ToList())
        {
            _boundsCache.Remove(kvp.Key);
            shifted.Add(new KeyValuePair<int, double>(kvp.Key + count, kvp.Value));
        }
        foreach (var kvp in shifted) _boundsCache[kvp.Key] = kvp.Value;
    }

    public void OnItemsRemoved(int index, int count)
    {
        if (_boundsCache.Count == 0) return;
        for (var i = 0; i < count; i++) _boundsCache.Remove(index + i);
        var shifted = new List<KeyValuePair<int, double>>();
        foreach (var kvp in _boundsCache.Where(k => k.Key >= index + count).OrderBy(k => k.Key).ToList())
        {
            _boundsCache.Remove(kvp.Key);
            shifted.Add(new KeyValuePair<int, double>(kvp.Key - count, kvp.Value));
        }
        foreach (var kvp in shifted) _boundsCache[kvp.Key] = kvp.Value;
    }

    public void OnItemsReset()
    {
        _boundsCache.Clear();
    }

    public (int StartIndex, int EndIndex) RealizedRange { get; private set; }

    #region IScrollInfo Implementation

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public ScrollViewer ScrollOwner { get; set; } = null!;

    private double _extentWidth;
    private double _extentHeight;
    private double _viewportWidth;
    private double _viewportHeight;
    private double _horizontalOffset;
    private double _verticalOffset;

    public double ExtentWidth => _extentWidth;
    public double ExtentHeight => _extentHeight;
    public double ViewportWidth => _viewportWidth;
    public double ViewportHeight => _viewportHeight;
    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;

    public void SetVerticalOffset(double offset)
    {
        var (generator, items, _) = ResolveOwnerInfo();
        var itemCount = items?.Count ?? 0;
        var maxOffset = _extentHeight > 0
            ? Math.Max(0, _extentHeight - _viewportHeight)
            : (itemCount > 0 ? Math.Max(0, GetTotalExtent(itemCount) - (_viewportHeight > 0 ? _viewportHeight : 200)) : double.PositiveInfinity);
        var clamped = Math.Max(0, Math.Min(offset, maxOffset));
        if (Math.Abs(_verticalOffset - clamped) < 0.0001) return;
        _verticalOffset = clamped;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetHorizontalOffset(double offset)
    {
        var (generator, items, _) = ResolveOwnerInfo();
        var itemCount = items?.Count ?? 0;
        var maxOffset = _extentWidth > 0
            ? Math.Max(0, _extentWidth - _viewportWidth)
            : (itemCount > 0 ? Math.Max(0, GetTotalExtent(itemCount) - (_viewportWidth > 0 ? _viewportWidth : 200)) : double.PositiveInfinity);
        var clamped = Math.Max(0, Math.Min(offset, maxOffset));
        if (Math.Abs(_horizontalOffset - clamped) < 0.0001) return;
        _horizontalOffset = clamped;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void LineUp() => SetVerticalOffset(_verticalOffset - (VirtualizingPanel.GetScrollUnit(this) == ScrollUnit.Item ? _estimatedItemDimension : 16.0));
    public void LineDown() => SetVerticalOffset(_verticalOffset + (VirtualizingPanel.GetScrollUnit(this) == ScrollUnit.Item ? _estimatedItemDimension : 16.0));
    public void LineLeft() => SetHorizontalOffset(_horizontalOffset - 16.0);
    public void LineRight() => SetHorizontalOffset(_horizontalOffset + 16.0);

    public void PageUp() => SetVerticalOffset(_verticalOffset - Math.Max(_estimatedItemDimension, _viewportHeight));
    public void PageDown() => SetVerticalOffset(_verticalOffset + Math.Max(_estimatedItemDimension, _viewportHeight));
    public void PageLeft() => SetHorizontalOffset(_horizontalOffset - Math.Max(16.0, _viewportWidth));
    public void PageRight() => SetHorizontalOffset(_horizontalOffset + Math.Max(16.0, _viewportWidth));

    public void MouseWheelUp() => SetVerticalOffset(_verticalOffset - _estimatedItemDimension * 3);
    public void MouseWheelDown() => SetVerticalOffset(_verticalOffset + _estimatedItemDimension * 3);
    public void MouseWheelLeft() => SetHorizontalOffset(_horizontalOffset - 48.0);
    public void MouseWheelRight() => SetHorizontalOffset(_horizontalOffset + 48.0);

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    #endregion

    private (int startIndex, int endIndex) CalculateRealizationWindow(int itemCount, double offset, double viewport)
    {
        if (itemCount == 0) return (0, 0);

        var current = 0.0;
        var start = 0;
        while (start < itemCount)
        {
            var dim = GetItemBounds(start);
            if (current + dim > offset) break;
            current += dim;
            start++;
        }
        if (start >= itemCount) start = Math.Max(0, itemCount - 1);

        var end = start;
        var endPos = current;
        while (end < itemCount && endPos < offset + viewport)
        {
            endPos += GetItemBounds(end);
            end++;
        }
        end = Math.Min(itemCount, end + 1); // 1 extra buffer item
        start = Math.Max(0, start - 1);     // 1 extra buffer item

        return (start, end);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var (generator, items, ownerVisual) = ResolveOwnerInfo();
        var isVirtualizing = GetIsVirtualizing(this) && (ownerVisual is null || (VirtualizingPanel.GetIsVirtualizing(ownerVisual) && GetIsVirtualizing(ownerVisual)));

        if (generator is null || items is null || !isVirtualizing)
        {
            if (generator is not null && items is not null)
            {
                for (var i = 0; i < items.Count; i++) generator.ContainerFromIndex(i);
            }
            RealizedRange = (0, Children.Count);
            return MeasureNonVirtualizing(available);
        }

        var itemCount = items.Count;
        if (itemCount == 0)
        {
            _extentHeight = 0;
            _extentWidth = 0;
            _viewportHeight = available.Height;
            _viewportWidth = available.Width;
            RealizedRange = (0, 0);
            return new WpfSize(0, 0);
        }

        if (Orientation == Orientation.Vertical)
        {
            var viewport = available.Height;
            if (double.IsPositiveInfinity(viewport) || double.IsNaN(viewport))
            {
                if (!double.IsNaN(Height)) viewport = Height;
                else if (ownerVisual is not null && !double.IsNaN(ownerVisual.Height)) viewport = ownerVisual.Height;
                else if (_viewportHeight > 0 && !double.IsPositiveInfinity(_viewportHeight)) viewport = _viewportHeight;
                else if (ScrollOwner is not null && ScrollOwner.ViewportHeight > 0) viewport = ScrollOwner.ViewportHeight;
            }

            if (double.IsPositiveInfinity(viewport) || double.IsNaN(viewport))
            {
                for (var i = 0; i < itemCount; i++) generator.ContainerFromIndex(i);
                RealizedRange = (0, itemCount);
                return MeasureNonVirtualizing(available);
            }

            var (start, end) = CalculateRealizationWindow(itemCount, _verticalOffset, viewport);
            RealizedRange = (start, end);

            var mode = GetVirtualizationMode(this);
            if (mode == VirtualizationMode.Standard && ownerVisual is not null)
                mode = GetVirtualizationMode(ownerVisual);
            var isRecycling = mode == VirtualizationMode.Recycling || generator.RecyclingEnabled;

            // De-realize containers outside window
            for (var i = 0; i < itemCount; i++)
            {
                if ((i < start || i >= end) && generator.IsRealized(i))
                {
                    if (isRecycling) generator.Recycle(i);
                    else generator.Remove(i);
                }
            }

            // Realize and measure containers inside window
            var maxCross = 0.0;
            var availableWidth = double.IsPositiveInfinity(available.Width) ? double.PositiveInfinity : available.Width;
            for (var i = start; i < end; i++)
            {
                var container = (UIElement?)generator.ContainerFromIndex(i);
                if (container is not null)
                {
                    var m = GetMargin(container);
                    var childAvailableWidth = double.IsPositiveInfinity(availableWidth)
                        ? double.PositiveInfinity
                        : Math.Max(0, availableWidth - m.Left - m.Right);
                    container.Measure(new WpfSize(childAvailableWidth, double.PositiveInfinity));
                    var rawH = container.DesiredSize.Height + m.Top + m.Bottom;
                    var h = rawH > 0 ? rawH : _estimatedItemDimension;
                    var w = container.DesiredSize.Width + m.Left + m.Right;
                    _boundsCache[i] = h;
                    if (rawH > 0) _estimatedItemDimension = rawH;
                    maxCross = Math.Max(maxCross, w);
                }
            }

            var totalExtent = GetTotalExtent(itemCount);
            _extentHeight = totalExtent;
            _extentWidth = maxCross;
            _viewportHeight = viewport;
            _viewportWidth = double.IsPositiveInfinity(available.Width) ? maxCross : available.Width;

            var resultWidth = double.IsPositiveInfinity(available.Width) ? maxCross : Math.Min(available.Width, maxCross);
            var resultHeight = Math.Min(viewport, totalExtent);
            return new WpfSize(resultWidth, resultHeight);
        }
        else // Horizontal
        {
            var viewport = available.Width;
            if (double.IsPositiveInfinity(viewport) || double.IsNaN(viewport))
            {
                if (!double.IsNaN(Width)) viewport = Width;
                else if (ownerVisual is not null && !double.IsNaN(ownerVisual.Width)) viewport = ownerVisual.Width;
                else if (_viewportWidth > 0 && !double.IsPositiveInfinity(_viewportWidth)) viewport = _viewportWidth;
                else if (ScrollOwner is not null && ScrollOwner.ViewportWidth > 0) viewport = ScrollOwner.ViewportWidth;
            }

            if (double.IsPositiveInfinity(viewport) || double.IsNaN(viewport))
            {
                for (var i = 0; i < itemCount; i++) generator.ContainerFromIndex(i);
                RealizedRange = (0, itemCount);
                return MeasureNonVirtualizing(available);
            }

            var (start, end) = CalculateRealizationWindow(itemCount, _horizontalOffset, viewport);
            RealizedRange = (start, end);

            var mode = GetVirtualizationMode(this);
            if (mode == VirtualizationMode.Standard && ownerVisual is not null)
                mode = GetVirtualizationMode(ownerVisual);
            var isRecycling = mode == VirtualizationMode.Recycling || generator.RecyclingEnabled;

            for (var i = 0; i < itemCount; i++)
            {
                if ((i < start || i >= end) && generator.IsRealized(i))
                {
                    if (isRecycling) generator.Recycle(i);
                    else generator.Remove(i);
                }
            }

            var maxCross = 0.0;
            var availableHeight = double.IsPositiveInfinity(available.Height) ? double.PositiveInfinity : available.Height;
            for (var i = start; i < end; i++)
            {
                var container = (UIElement?)generator.ContainerFromIndex(i);
                if (container is not null)
                {
                    var m = GetMargin(container);
                    var childAvailableHeight = double.IsPositiveInfinity(availableHeight)
                        ? double.PositiveInfinity
                        : Math.Max(0, availableHeight - m.Top - m.Bottom);
                    container.Measure(new WpfSize(double.PositiveInfinity, childAvailableHeight));
                    var rawW = container.DesiredSize.Width + m.Left + m.Right;
                    var w = rawW > 0 ? rawW : _estimatedItemDimension;
                    var h = container.DesiredSize.Height + m.Top + m.Bottom;
                    _boundsCache[i] = w;
                    if (rawW > 0) _estimatedItemDimension = rawW;
                    maxCross = Math.Max(maxCross, h);
                }
            }

            var totalExtent = GetTotalExtent(itemCount);
            _extentWidth = totalExtent;
            _extentHeight = maxCross;
            _viewportWidth = viewport;
            _viewportHeight = double.IsPositiveInfinity(available.Height) ? maxCross : available.Height;

            var resultHeight = double.IsPositiveInfinity(available.Height) ? maxCross : Math.Min(available.Height, maxCross);
            var resultWidth = Math.Min(viewport, totalExtent);
            return new WpfSize(resultWidth, resultHeight);
        }
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var (generator, items, ownerVisual) = ResolveOwnerInfo();
        var isVirtualizing = GetIsVirtualizing(this) && (ownerVisual is null || (VirtualizingPanel.GetIsVirtualizing(ownerVisual) && GetIsVirtualizing(ownerVisual)));

        if (generator is null || items is null || !isVirtualizing)
        {
            ArrangeNonVirtualizing(finalRect);
            return;
        }

        if (Orientation == Orientation.Vertical)
        {
            foreach (UIElement c in Children)
            {
                var item = generator.ItemFromContainer(c);
                var index = item is null ? -1 : items.IndexOf(item);
                if (index < 0) continue;

                var itemTop = 0.0;
                for (var k = 0; k < index; k++) itemTop += GetItemBounds(k);

                var y = finalRect.Y + itemTop - _verticalOffset;
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
            }
        }
        else // Horizontal
        {
            foreach (UIElement c in Children)
            {
                var item = generator.ItemFromContainer(c);
                var index = item is null ? -1 : items.IndexOf(item);
                if (index < 0) continue;

                var itemLeft = 0.0;
                for (var k = 0; k < index; k++) itemLeft += GetItemBounds(k);

                var x = finalRect.X + itemLeft - _horizontalOffset;
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
            }
        }
    }

    private WpfSize MeasureNonVirtualizing(WpfSize available)
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

    private void ArrangeNonVirtualizing(WpfRect finalRect)
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
