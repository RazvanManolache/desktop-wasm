namespace DesktopWasm.Wpf;

/// <summary>Overflow policy (matches System.Windows.Controls.OverflowMode).</summary>
public enum OverflowMode
{
    AsNeeded = 0,
    Always = 1,
    Never = 2,
}

/// <summary>
/// Tool strip (matches System.Windows.Controls.ToolBar): a HeaderedItemsControl
/// with WPF-identical band/orientation/overflow dependency properties and the
/// static ComponentResourceKey style keys converted themes read (upstream
/// AvalonDock reads ToolBar.ButtonStyleKey). Items lay out through the
/// inherited items panel; the overflow panel stays unexpanded
/// (HasOverflowItems false), IsOverflowOpen is retained-only like
/// AllowsTransparency, and ToolBarPanel/ToolBarOverflowPanel stay boundaries
/// (only the ButtonStyleKey surface plus the ToolBarTray style target are
/// converted-live so far).
/// Live-verified against PresentationFramework 9.0.21: read-only Orientation,
/// Band/BandIndex, read-only HasOverflowItems, attached IsOverflowItem and
/// OverflowMode, all eight *StyleKey properties.
/// </summary>
public class ToolBar : HeaderedItemsControl
{
    static ToolBar()
    {
        // Tool strips never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(ToolBar), new FrameworkPropertyMetadata(false));
    }

    private static readonly DependencyPropertyKey OrientationKey =
        DependencyProperty.RegisterReadOnly("Orientation", typeof(Orientation), typeof(ToolBar),
            new FrameworkPropertyMetadata(Orientation.Horizontal));

    public static readonly DependencyProperty OrientationProperty = OrientationKey.DependencyProperty;

    public static readonly DependencyProperty BandProperty =
        DependencyProperty.Register("Band", typeof(int), typeof(ToolBar),
            new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty BandIndexProperty =
        DependencyProperty.Register("BandIndex", typeof(int), typeof(ToolBar),
            new FrameworkPropertyMetadata(0));

    private static readonly DependencyPropertyKey HasOverflowItemsKey =
        DependencyProperty.RegisterReadOnly("HasOverflowItems", typeof(bool), typeof(ToolBar),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasOverflowItemsProperty = HasOverflowItemsKey.DependencyProperty;

    public static readonly DependencyProperty IsOverflowOpenProperty =
        DependencyProperty.Register("IsOverflowOpen", typeof(bool), typeof(ToolBar),
            new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey IsOverflowItemKey =
        DependencyProperty.RegisterAttachedReadOnly("IsOverflowItem", typeof(bool), typeof(ToolBar),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsOverflowItemProperty = IsOverflowItemKey.DependencyProperty;

    public static readonly DependencyProperty OverflowModeProperty =
        DependencyProperty.RegisterAttached("OverflowMode", typeof(OverflowMode), typeof(ToolBar),
            new FrameworkPropertyMetadata(OverflowMode.AsNeeded));

    public static ResourceKey ButtonStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "ButtonStyleKey");
    public static ResourceKey ToggleButtonStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "ToggleButtonStyleKey");
    public static ResourceKey SeparatorStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "SeparatorStyleKey");
    public static ResourceKey CheckBoxStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "CheckBoxStyleKey");
    public static ResourceKey RadioButtonStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "RadioButtonStyleKey");
    public static ResourceKey ComboBoxStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "ComboBoxStyleKey");
    public static ResourceKey TextBoxStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "TextBoxStyleKey");
    public static ResourceKey MenuStyleKey { get; } = new ComponentResourceKey(typeof(ToolBar), "MenuStyleKey");

    public Orientation Orientation => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Horizontal);

    public int Band
    {
        get => (int)(GetValue(BandProperty) ?? 0);
        set => SetValue(BandProperty, value);
    }

    public int BandIndex
    {
        get => (int)(GetValue(BandIndexProperty) ?? 0);
        set => SetValue(BandIndexProperty, value);
    }

    public bool IsOverflowOpen
    {
        get => (bool)(GetValue(IsOverflowOpenProperty) ?? false);
        set => SetValue(IsOverflowOpenProperty, value);
    }

    public bool HasOverflowItems => (bool)(GetValue(HasOverflowItemsProperty) ?? false);

    public static bool GetIsOverflowItem(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsOverflowItemProperty) ?? false);
    }

    public static void SetIsOverflowItem(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsOverflowItemKey, value);
    }

    public static OverflowMode GetOverflowMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (OverflowMode)(element.GetValue(OverflowModeProperty) ?? OverflowMode.AsNeeded);
    }

    public static void SetOverflowMode(DependencyObject element, OverflowMode mode)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OverflowModeProperty, mode);
    }
}

// Slice-146: HandyControl theme.baml ToolBarTray style-target routing.
//
// The keyed 'ToolBarTrayBaseStyle' entry (TargetType over well-known id -672
// with a single Background packed-ARGB setter) lands as a live engine Style.
// The shim mirrors the genuine WPF surface the staged entry needs: like real
// WPF (live-verified on net10.0-windows: ToolBarTray : FrameworkElement with
// Background/Orientation/IsLocked all ToolBarTray-owned, Background default
// null, Orientation default Horizontal, IsLocked default false) the engine
// tray reuses the shared Panel Background identity, so qualified Background
// setters resolve through DependencyProperty.FromName like WPF. Orientation
// and IsLocked stay follow-up work: no staged entry reads them yet, and the
// tray hosts no live children (ToolBarPanel/ToolBarOverflowPanel stay
// boundaries like before, so the tray paints nothing on its own).
/// <summary>
/// Tool strip tray (matches System.Windows.Controls.ToolBarTray): a
/// FrameworkElement host for tool strips carrying the shared Background
/// identity (null default, like WPF). The tray itself paints nothing; the
/// staged 'ToolBarTrayBaseStyle' style lands its Background setter here.
/// </summary>
/// <summary>Tool strip items panel
/// (matches System.Windows.Controls.Primitives.ToolBarPanel): a StackPanel
/// with no additional public API (live-verified against the native oracle:
/// no declared properties or dependency properties).</summary>
public class ToolBarPanel : StackPanel
{
}

/// <summary>Tool strip overflow panel
/// (matches System.Windows.Controls.Primitives.ToolBarOverflowPanel): stacks
/// items vertically and wraps into further columns when the column would
/// exceed the available height (or the finite WrapWidth, NaN default like
/// WPF, would be exceeded); items stretch to their column width.</summary>
public class ToolBarOverflowPanel : Panel
{
    public static readonly DependencyProperty WrapWidthProperty =
        DependencyProperty.Register("WrapWidth", typeof(double), typeof(ToolBarOverflowPanel),
            new FrameworkPropertyMetadata(double.NaN) { AffectsMeasure = true });

    public double WrapWidth
    {
        get => (double?)GetValue(WrapWidthProperty) ?? double.NaN;
        set => SetValue(WrapWidthProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var columns = LayoutColumns(constraint);
        var width = 0.0;
        var height = 0.0;
        foreach (var column in columns)
        {
            width += column.Width;
            height = Math.Max(height, column.Height);
        }
        if (!double.IsNaN(WrapWidth) && !double.IsInfinity(WrapWidth))
            width = Math.Min(width, WrapWidth);
        return new Size(
            double.IsInfinity(constraint.Width) ? width : Math.Min(width, constraint.Width),
            double.IsInfinity(constraint.Height) ? height : Math.Min(height, constraint.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = LayoutColumns(finalSize);
        var x = 0.0;
        foreach (var column in columns)
        {
            var y = 0.0;
            foreach (var index in column.Children)
            {
                var child = Children[index];
                var margin = child is FrameworkElement fe ? fe.Margin : new Thickness(0);
                var ha = child is FrameworkElement aligned ? aligned.HorizontalAlignment : HorizontalAlignment.Stretch;
                var slot = Math.Max(0, column.Width - margin.Left - margin.Right);
                var childWidth = ha == HorizontalAlignment.Stretch ? slot : Math.Min(slot, child.DesiredSize.Width);
                var offset = ha switch
                {
                    HorizontalAlignment.Center => (slot - childWidth) / 2,
                    HorizontalAlignment.Right => slot - childWidth,
                    _ => 0.0,
                };
                child.Arrange(new WpfRect(x + margin.Left + offset, y + margin.Top, childWidth, child.DesiredSize.Height));
                y += margin.Top + child.DesiredSize.Height + margin.Bottom;
            }
            x += column.Width;
        }
        return finalSize;
    }

    private readonly struct Column
    {
        public Column(double width, double height, List<int> children)
        {
            Width = width;
            Height = height;
            Children = children;
        }

        public double Width { get; }
        public double Height { get; }
        public List<int> Children { get; }
    }

    private List<Column> LayoutColumns(Size constraint)
    {
        var columns = new List<Column>();
        var current = new List<int>();
        var columnWidth = 0.0;
        var columnHeight = 0.0;
        var wrapWidth = WrapWidth;
        var index = 0;
        foreach (UIElement child in Children)
        {
            var margin = child is FrameworkElement fe ? fe.Margin : new Thickness(0);
            var measureWidth = double.IsInfinity(constraint.Width) ? double.PositiveInfinity
                : Math.Max(0, constraint.Width - margin.Left - margin.Right);
            child.Measure(new WpfSize(measureWidth, double.PositiveInfinity));
            var childWidth = child.DesiredSize.Width + margin.Left + margin.Right;
            var childHeight = child.DesiredSize.Height + margin.Top + margin.Bottom;
            var heightBound = double.IsInfinity(constraint.Height) ? double.PositiveInfinity : constraint.Height;
            var widthBound = !double.IsNaN(wrapWidth) && !double.IsInfinity(wrapWidth)
                ? wrapWidth : double.PositiveInfinity;
            var overflows = current.Count > 0 &&
                (columnHeight + childHeight > heightBound || columnWidth + childWidth > widthBound);
            if (overflows)
            {
                columns.Add(new Column(columnWidth, columnHeight, current));
                current = new List<int>();
                columnWidth = 0.0;
                columnHeight = 0.0;
            }
            current.Add(index);
            columnWidth = Math.Max(columnWidth, childWidth);
            columnHeight += childHeight;
            index++;
        }
        if (current.Count > 0) columns.Add(new Column(columnWidth, columnHeight, current));
        return columns;
    }
}

public class ToolBarTray : FrameworkElement
{
    /// <summary>
    /// Fill identity (matches System.Windows.Controls.ToolBarTray.BackgroundProperty;
    /// the shared Panel instance genuine WPF reuses for the tray, so qualified
    /// style setters bind; the CLR wrapper stays store-backed).
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty = Panel.BackgroundProperty;

    /// <summary>
    /// Layout direction (matches ToolBarTray.OrientationProperty; Horizontal
    /// default like WPF; retained-only — the tray paints nothing on its own).
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register("Orientation", typeof(Orientation), typeof(ToolBarTray),
            new FrameworkPropertyMetadata(Orientation.Horizontal));

    /// <summary>
    /// Lock flag (matches ToolBarTray.IsLockedProperty; false default like
    /// WPF; theme triggers read it for the overflow chevron).
    /// </summary>
    public static readonly DependencyProperty IsLockedProperty =
        DependencyProperty.Register("IsLocked", typeof(bool), typeof(ToolBarTray),
            new FrameworkPropertyMetadata(false));

    /// <summary>Layout direction (store-backed).</summary>
    public Orientation Orientation
    {
        get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Horizontal);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Lock flag (store-backed).</summary>
    public bool IsLocked
    {
        get => (bool)(GetValue(IsLockedProperty) ?? false);
        set => SetValue(IsLockedProperty, value);
    }

    /// <summary>Fill brush (null paints nothing, like WPF's null default).</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set
        {
            if (ReferenceEquals(GetValue(BackgroundProperty), value)) return;
            SetValue(BackgroundProperty, value);
            InvalidateVisual();
        }
    }
}
