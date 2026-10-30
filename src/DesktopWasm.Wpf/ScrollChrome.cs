using SkiaSharp;

namespace DesktopWasm.Wpf;

// Slice-69 MaterialDesignDemo entry prerequisites: the scroll chrome the
// Material themes drive (ScrollBar.Scroll/add_remove_Scroll/set_ViewportSize
// plus the CustomPopupPlacement callback shape the popup themes use),
// the exception-validation marker, the XmlLanguage culture bridge and the
// Fonts enumeration the demo's font picker binds (get_SystemFontFamilies).
// Member evidence via Mono.Cecil on the upstream net8.0-windows
// MaterialDesignDemo.dll/MaterialDesignDemo.Shared.dll/
// MaterialDesignThemes.Wpf.dll binaries; every enum value, default, ctor
// shape and validation rule below was verified live against the 9.0.21
// WindowsDesktop runtime before it was pinned.

/// <summary>Scroll gesture kind (matches System.Windows.Controls.Primitives.ScrollEventType).</summary>
public enum ScrollEventType
{
    EndScroll = 0,
    First = 1,
    LargeDecrement = 2,
    LargeIncrement = 3,
    Last = 4,
    SmallDecrement = 5,
    SmallIncrement = 6,
    ThumbPosition = 7,
    ThumbTrack = 8,
}

/// <summary>Scroll notification payload (matches System.Windows.Controls.Primitives.ScrollEventArgs).</summary>
public class ScrollEventArgs : RoutedEventArgs
{
    public ScrollEventArgs(ScrollEventType scrollEventType, double newValue)
    {
        ScrollEventType = scrollEventType;
        NewValue = newValue;
    }

    /// <summary>Which gesture completed.</summary>
    public ScrollEventType ScrollEventType { get; }

    /// <summary>Value after the scroll.</summary>
    public double NewValue { get; }
}

/// <summary>Scroll handler shape (matches System.Windows.Controls.Primitives.ScrollEventHandler).</summary>
public delegate void ScrollEventHandler(object sender, ScrollEventArgs e);

/// <summary>
/// Scroll bar (matches System.Windows.Controls.Primitives.ScrollBar):
/// a RangeBase with an Orientation (Vertical default, like WPF), a
/// non-negative ViewportSize (negative/NaN sets throw ArgumentException,
/// like WPF's validate callback), a template-supplied Track (null until
/// a template provides PART_Track, like WPF) and a Scroll routed event.
/// Every committed value change reports EndScroll with the new value;
/// per-gesture ThumbTrack streaming stays a boundary (no template-part
/// drag path yet), so programmatic and input-driven sets report the
/// same completed gesture. Paints a groove plus a viewport-proportioned
/// thumb on both renderers.
/// </summary>
public class ScrollBar : RangeBase
{
    /// <summary>Scroll token (matches ScrollBar.ScrollEvent; bubbles like WPF).</summary>
    public static readonly RoutedEvent ScrollEvent = EventManager.RegisterRoutedEvent(
        "Scroll", RoutingStrategy.Bubble, typeof(ScrollEventHandler), typeof(ScrollBar));

    /// <summary>Line-up command (matches ScrollBar.LineUpCommand; repeat buttons bind it).</summary>
    public static readonly RoutedCommand LineUpCommand = new(nameof(LineUpCommand), typeof(ScrollBar));

    /// <summary>Line-down command (matches ScrollBar.LineDownCommand).</summary>
    public static readonly RoutedCommand LineDownCommand = new(nameof(LineDownCommand), typeof(ScrollBar));

    /// <summary>Line-left command (matches ScrollBar.LineLeftCommand).</summary>
    public static readonly RoutedCommand LineLeftCommand = new(nameof(LineLeftCommand), typeof(ScrollBar));

    /// <summary>Line-right command (matches ScrollBar.LineRightCommand).</summary>
    public static readonly RoutedCommand LineRightCommand = new(nameof(LineRightCommand), typeof(ScrollBar));

    /// <summary>Page-up command (matches ScrollBar.PageUpCommand).</summary>
    public static readonly RoutedCommand PageUpCommand = new(nameof(PageUpCommand), typeof(ScrollBar));

    /// <summary>Page-down command (matches ScrollBar.PageDownCommand).</summary>
    public static readonly RoutedCommand PageDownCommand = new(nameof(PageDownCommand), typeof(ScrollBar));

    /// <summary>Page-left command (matches ScrollBar.PageLeftCommand).</summary>
    public static readonly RoutedCommand PageLeftCommand = new(nameof(PageLeftCommand), typeof(ScrollBar));

    /// <summary>Page-right command (matches ScrollBar.PageRightCommand).</summary>
    public static readonly RoutedCommand PageRightCommand = new(nameof(PageRightCommand), typeof(ScrollBar));

    /// <summary>Scroll-here command (matches ScrollBar.ScrollHereCommand).</summary>
    public static readonly RoutedCommand ScrollHereCommand = new(nameof(ScrollHereCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-top command (matches ScrollBar.ScrollToTopCommand).</summary>
    public static readonly RoutedCommand ScrollToTopCommand = new(nameof(ScrollToTopCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-bottom command (matches ScrollBar.ScrollToBottomCommand).</summary>
    public static readonly RoutedCommand ScrollToBottomCommand = new(nameof(ScrollToBottomCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-left-end command (matches ScrollBar.ScrollToLeftEndCommand).</summary>
    public static readonly RoutedCommand ScrollToLeftEndCommand = new(nameof(ScrollToLeftEndCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-right-end command (matches ScrollBar.ScrollToRightEndCommand).</summary>
    public static readonly RoutedCommand ScrollToRightEndCommand = new(nameof(ScrollToRightEndCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-home command (matches ScrollBar.ScrollToHomeCommand).</summary>
    public static readonly RoutedCommand ScrollToHomeCommand = new(nameof(ScrollToHomeCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-end command (matches ScrollBar.ScrollToEndCommand).</summary>
    public static readonly RoutedCommand ScrollToEndCommand = new(nameof(ScrollToEndCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-offset commands (match ScrollBar.ScrollToHorizontalOffsetCommand/ScrollToVerticalOffsetCommand).</summary>
    public static readonly RoutedCommand ScrollToHorizontalOffsetCommand = new(nameof(ScrollToHorizontalOffsetCommand), typeof(ScrollBar));

    /// <summary>Scroll-to-offset command (matches ScrollBar.ScrollToVerticalOffsetCommand).</summary>
    public static readonly RoutedCommand ScrollToVerticalOffsetCommand = new(nameof(ScrollToVerticalOffsetCommand), typeof(ScrollBar));

    /// <summary>Deferred horizontal scroll command (matches ScrollBar.DeferScrollToHorizontalOffsetCommand).</summary>
    public static readonly RoutedCommand DeferScrollToHorizontalOffsetCommand = new(nameof(DeferScrollToHorizontalOffsetCommand), typeof(ScrollBar));

    /// <summary>Deferred vertical scroll command (matches ScrollBar.DeferScrollToVerticalOffsetCommand).</summary>
    public static readonly RoutedCommand DeferScrollToVerticalOffsetCommand = new(nameof(DeferScrollToVerticalOffsetCommand), typeof(ScrollBar));

    /// <summary>Orientation token (matches ScrollBar.OrientationProperty; Vertical default).</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        "Orientation", typeof(Orientation), typeof(ScrollBar),
        new FrameworkPropertyMetadata(Orientation.Vertical));

    /// <summary>Viewport token (matches ScrollBar.ViewportSizeProperty; 0 default, rejects negatives/NaN).</summary>
    public static readonly DependencyProperty ViewportSizeProperty = DependencyProperty.Register(
        "ViewportSize", typeof(double), typeof(ScrollBar),
        new FrameworkPropertyMetadata(0.0),
        static value => value is double d && !double.IsNaN(d) && d >= 0);

    /// <summary>Track direction (Vertical default, like WPF).</summary>
    public Orientation Orientation
    {
        get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Vertical);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Visible span (never negative or NaN; invalid sets throw ArgumentException, like WPF).</summary>
    public double ViewportSize
    {
        get => (double)(GetValue(ViewportSizeProperty) ?? 0.0);
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Value-mapping track (null until a template supplies PART_Track,
    /// like WPF; the engine has no template-part inflation yet).
    /// </summary>
    public Track? Track { get; private set; }

    /// <summary>Scroll completion notification (routed in real WPF; direct here).</summary>
    public event ScrollEventHandler? Scroll;

    /// <summary>Attaches the template track part (template inflation calls this when PART_Track resolves).</summary>
    internal void SetTrack(Track? track) => Track = track;

    /// <summary>Raises Scroll (matches ScrollBar.OnScroll).</summary>
    protected virtual void OnScroll(ScrollEventArgs e) => Scroll?.Invoke(this, e);

    /// <summary>Committed changes report EndScroll after the ValueChanged contract runs.</summary>
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        OnScroll(new ScrollEventArgs(ScrollEventType.EndScroll, newValue)
        {
            RoutedEvent = ScrollEvent,
            Source = this,
            OriginalSource = this,
        });
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        const double thickness = 17;
        const double length = 200;
        if (Orientation == Orientation.Horizontal)
            return new WpfSize(
                double.IsNaN(Width) ? Math.Min(available.Width, length) : Width,
                double.IsNaN(Height) ? thickness : Height);
        return new WpfSize(
            double.IsNaN(Width) ? thickness : Width,
            double.IsNaN(Height) ? Math.Min(available.Height, length) : Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var enabled = IsEffectivelyEnabled;
        var groove = enabled ? 0xD8D8D8FFu : 0xCCCCCCFFu;
        var thumbFill = !enabled ? 0xCCCCCCFFu : 0xFFFFFFFFu;
        var thumbEdge = enabled ? 0x707070FFu : 0x909090FFu;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, groove);
        var thumb = ThumbRect();
        if (thumb.Width <= 0 || thumb.Height <= 0) return;
        dc.DrawRectangle((float)thumb.X, (float)thumb.Y, (float)thumb.Width, (float)thumb.Height, thumbEdge);
        if (thumb.Width > 2 && thumb.Height > 2)
            dc.DrawRectangle((float)thumb.X + 1, (float)thumb.Y + 1,
                (float)thumb.Width - 2, (float)thumb.Height - 2, thumbFill);
    }

    /// <summary>Thumb box: viewport-proportioned, positioned by value.</summary>
    internal WpfRect ThumbRect()
    {
        var r = RenderRect;
        var range = Maximum - Minimum;
        var extent = range + Math.Max(0, ViewportSize);
        double fraction = extent <= 0 ? 1 : Math.Clamp(ViewportSize / extent, 0, 1);
        double t = range <= 0 ? 0 : (Value - Minimum) / range;
        if (Orientation == Orientation.Horizontal)
        {
            var w = Math.Max(12, r.Width * fraction);
            return new WpfRect(r.X + t * Math.Max(0, r.Width - w), r.Y, w, r.Height);
        }
        var h = Math.Max(12, r.Height * fraction);
        return new WpfRect(r.X, r.Y + t * Math.Max(0, r.Height - h), r.Width, h);
    }
}

/// <summary>
/// Value-mapping track (matches System.Windows.Controls.Primitives.Track):
/// hosts the DecreaseRepeatButton/Thumb/IncreaseRepeatButton parts,
/// maps points and drag distances to values with real orientation math,
/// and arranges the parts proportionally (decrease share by value
/// position, thumb share by viewport, increase takes the rest).
/// The engine has no template-part inflation yet, so parts arrive via
/// the CLR (null until set, like an untemplated WPF track).
/// </summary>
public class Track : FrameworkElement
{
    /// <summary>Orientation token (matches Track.OrientationProperty; Horizontal default).</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        "Orientation", typeof(Orientation), typeof(Track),
        new FrameworkPropertyMetadata(Orientation.Horizontal));

    /// <summary>Lower-bound token (matches Track.MinimumProperty).</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        "Minimum", typeof(double), typeof(Track), new FrameworkPropertyMetadata(0.0));

    /// <summary>Upper-bound token (matches Track.MaximumProperty).</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        "Maximum", typeof(double), typeof(Track), new FrameworkPropertyMetadata(1.0));

    /// <summary>Current-value token (matches Track.ValueProperty; coerced into range).</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "Value", typeof(double), typeof(Track), new FrameworkPropertyMetadata(0.0));

    /// <summary>Viewport token (matches Track.ViewportSizeProperty).</summary>
    public static readonly DependencyProperty ViewportSizeProperty = DependencyProperty.Register(
        "ViewportSize", typeof(double), typeof(Track),
        new FrameworkPropertyMetadata(double.NaN),
        static value => value is double d && (double.IsNaN(d) || d >= 0));

    /// <summary>Direction token (matches Track.IsDirectionReversedProperty).</summary>
    public static readonly DependencyProperty IsDirectionReversedProperty = DependencyProperty.Register(
        "IsDirectionReversed", typeof(bool), typeof(Track), new FrameworkPropertyMetadata(false));

    /// <summary>Track direction (Horizontal default, like WPF).</summary>
    public Orientation Orientation
    {
        get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Horizontal);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Lower bound (coerces Value into range).</summary>
    public double Minimum
    {
        get => (double)(GetValue(MinimumProperty) ?? 0.0);
        set { SetValue(MinimumProperty, value); Value = Value; }
    }

    /// <summary>Upper bound (coerces Value into range).</summary>
    public double Maximum
    {
        get => (double)(GetValue(MaximumProperty) ?? 1.0);
        set { SetValue(MaximumProperty, value); Value = Value; }
    }

    /// <summary>Current value (clamped into [Minimum, Maximum]).</summary>
    public double Value
    {
        get => (double)(GetValue(ValueProperty) ?? 0.0);
        set => SetValue(ValueProperty, Math.Clamp(value, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum)));
    }

    /// <summary>Visible span (NaN means unbounded, like WPF).</summary>
    public double ViewportSize
    {
        get => (double)(GetValue(ViewportSizeProperty) ?? double.NaN);
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>Swaps min/max ends (value mapping and layout follow).</summary>
    public bool IsDirectionReversed
    {
        get => (bool)(GetValue(IsDirectionReversedProperty) ?? false);
        set => SetValue(IsDirectionReversedProperty, value);
    }

    private RepeatButton? _decreaseRepeatButton;
    private Thumb? _thumb;
    private RepeatButton? _increaseRepeatButton;

    /// <summary>Leading repeat button (null until a template provides it; setting parents the part).</summary>
    public RepeatButton? DecreaseRepeatButton
    {
        get => _decreaseRepeatButton;
        set
        {
            _decreaseRepeatButton = value;
            if (value is not null) AddChild(value);
        }
    }

    /// <summary>Drag thumb (null until a template provides it; setting parents the part).</summary>
    public Thumb? Thumb
    {
        get => _thumb;
        set
        {
            _thumb = value;
            if (value is not null) AddChild(value);
        }
    }

    /// <summary>Trailing repeat button (null until a template provides it; setting parents the part).</summary>
    public RepeatButton? IncreaseRepeatButton
    {
        get => _increaseRepeatButton;
        set
        {
            _increaseRepeatButton = value;
            if (value is not null) AddChild(value);
        }
    }

    private double Range => Maximum - Minimum;

    private double Position
    {
        get
        {
            if (Range <= 0) return 0;
            var t = (Value - Minimum) / Range;
            return IsDirectionReversed ? 1 - t : t;
        }
    }

    /// <summary>Range value under a point (matches Track.ValueFromPoint).</summary>
    public double ValueFromPoint(Point point)
    {
        var r = RenderRect;
        double t = Orientation == Orientation.Horizontal
            ? (r.Width <= 0 ? 0 : (point.X - r.X) / r.Width)
            : (r.Height <= 0 ? 0 : (point.Y - r.Y) / r.Height);
        if (IsDirectionReversed) t = 1 - t;
        return Minimum + t * Range;
    }

    /// <summary>Value delta for a drag distance (matches Track.ValueFromDistance).</summary>
    public double ValueFromDistance(double horizontal, double vertical)
    {
        var r = RenderRect;
        var track = Orientation == Orientation.Horizontal ? r.Width : r.Height;
        if (track <= 0 || Range == 0) return 0;
        var distance = Orientation == Orientation.Horizontal ? horizontal : vertical;
        var delta = distance / track * Range;
        return IsDirectionReversed ? -delta : delta;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        foreach (var part in Parts())
            part.Measure(available);
        double width = 0, height = 0;
        foreach (var part in Parts())
        {
            width = Math.Max(width, part.DesiredSize.Width);
            height = Math.Max(height, part.DesiredSize.Height);
        }
        return Orientation == Orientation.Horizontal
            ? new WpfSize(Math.Max(width, 40), Math.Max(height, 12))
            : new WpfSize(Math.Max(width, 12), Math.Max(height, 40));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var parts = Parts().ToList();
        if (parts.Count == 0) return;
        var extent = Range + (double.IsNaN(ViewportSize) ? 0 : Math.Max(0, ViewportSize));
        double thumbShare = extent <= 0 ? 1 : Math.Clamp((double.IsNaN(ViewportSize) ? 0 : ViewportSize) / extent, 0, 1);
        double thumbLength = Orientation == Orientation.Horizontal
            ? Math.Max(12, finalRect.Width * thumbShare)
            : Math.Max(12, finalRect.Height * thumbShare);
        var t = Position;
        if (Orientation == Orientation.Horizontal)
        {
            var free = Math.Max(0, finalRect.Width - thumbLength);
            var dx = finalRect.X + t * free;
            ArrangeHorizontal(parts, finalRect, dx, thumbLength);
        }
        else
        {
            var free = Math.Max(0, finalRect.Height - thumbLength);
            var dy = finalRect.Y + t * free;
            ArrangeVertical(parts, finalRect, dy, thumbLength);
        }
    }

    private IEnumerable<FrameworkElement> Parts()
    {
        if (DecreaseRepeatButton is not null) yield return DecreaseRepeatButton;
        if (Thumb is not null) yield return Thumb;
        if (IncreaseRepeatButton is not null) yield return IncreaseRepeatButton;
    }

    private void ArrangeHorizontal(List<FrameworkElement> parts, WpfRect slot, double thumbX, double thumbLength)
    {
        foreach (var part in parts)
        {
            double x, w;
            if (ReferenceEquals(part, Thumb)) { x = thumbX; w = thumbLength; }
            else if (ReferenceEquals(part, DecreaseRepeatButton)) { x = slot.X; w = Math.Max(0, thumbX - slot.X); }
            else { x = thumbX + thumbLength; w = Math.Max(0, slot.X + slot.Width - x); }
            part.Arrange(new WpfRect(x, slot.Y, w, slot.Height));
        }
    }

    private void ArrangeVertical(List<FrameworkElement> parts, WpfRect slot, double thumbY, double thumbLength)
    {
        foreach (var part in parts)
        {
            double y, h;
            if (ReferenceEquals(part, Thumb)) { y = thumbY; h = thumbLength; }
            else if (ReferenceEquals(part, DecreaseRepeatButton)) { y = slot.Y; h = Math.Max(0, thumbY - slot.Y); }
            else { y = thumbY + thumbLength; h = Math.Max(0, slot.Y + slot.Height - y); }
            part.Arrange(new WpfRect(slot.X, y, slot.Width, h));
        }
    }
}

/// <summary>Tick side (matches System.Windows.Controls.Primitives.TickBarPlacement).</summary>
public enum TickBarPlacement
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Slider tick marks (matches System.Windows.Controls.Primitives.TickBar:
/// Fill/Minimum/Maximum/TickFrequency/Ticks/Placement/ReservedSpace/IsDirectionReversed
/// verified live against the native oracle; ticks render as filled bars through the
/// retained DrawRectangle op, positioned along the track with pixel end-reservation
/// like native).</summary>
public class TickBar : FrameworkElement
{
    /// <summary>Tick paint (matches TickBar.FillProperty).</summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register("Fill", typeof(Brush), typeof(TickBar),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Track start (matches TickBar.MinimumProperty).</summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register("Minimum", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(0.0) { AffectsRender = true });

    /// <summary>Track end (matches TickBar.MaximumProperty).</summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register("Maximum", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(1.0) { AffectsRender = true });

    /// <summary>Selection start (matches TickBar.SelectionStartProperty; retained).</summary>
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register("SelectionStart", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(0.0) { AffectsRender = true });

    /// <summary>Selection end (matches TickBar.SelectionEndProperty; retained).</summary>
    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register("SelectionEnd", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(0.0) { AffectsRender = true });

    /// <summary>Auto tick step (matches TickBar.TickFrequencyProperty).</summary>
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register("TickFrequency", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(0.0) { AffectsRender = true });

    /// <summary>Explicit ticks (matches TickBar.TicksProperty).</summary>
    public static readonly DependencyProperty TicksProperty =
        DependencyProperty.Register("Ticks", typeof(DoubleCollection), typeof(TickBar),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Tick side (matches TickBar.PlacementProperty).</summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register("Placement", typeof(TickBarPlacement), typeof(TickBar),
            new FrameworkPropertyMetadata(TickBarPlacement.Top) { AffectsRender = true });

    /// <summary>Reversed direction (matches TickBar.IsDirectionReversedProperty).</summary>
    public static readonly DependencyProperty IsDirectionReversedProperty =
        DependencyProperty.Register("IsDirectionReversed", typeof(bool), typeof(TickBar),
            new FrameworkPropertyMetadata(false) { AffectsRender = true });

    /// <summary>Pixel inset at each track end (matches TickBar.ReservedSpaceProperty).</summary>
    public static readonly DependencyProperty ReservedSpaceProperty =
        DependencyProperty.Register("ReservedSpace", typeof(double), typeof(TickBar),
            new FrameworkPropertyMetadata(0.0) { AffectsRender = true });

    /// <summary>Tick paint.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Track start.</summary>
    public double Minimum
    {
        get => (double)(GetValue(MinimumProperty) ?? 0.0);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Track end.</summary>
    public double Maximum
    {
        get => (double)(GetValue(MaximumProperty) ?? 1.0);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Selection start (retained).</summary>
    public double SelectionStart
    {
        get => (double)(GetValue(SelectionStartProperty) ?? 0.0);
        set => SetValue(SelectionStartProperty, value);
    }

    /// <summary>Selection end (retained).</summary>
    public double SelectionEnd
    {
        get => (double)(GetValue(SelectionEndProperty) ?? 0.0);
        set => SetValue(SelectionEndProperty, value);
    }

    /// <summary>Auto tick step (0 = explicit ticks only).</summary>
    public double TickFrequency
    {
        get => (double)(GetValue(TickFrequencyProperty) ?? 0.0);
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>Explicit tick positions.</summary>
    public DoubleCollection? Ticks
    {
        get => (DoubleCollection?)GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    /// <summary>Tick side.</summary>
    public TickBarPlacement Placement
    {
        get => (TickBarPlacement)(GetValue(PlacementProperty) ?? TickBarPlacement.Top);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Reversed direction.</summary>
    public bool IsDirectionReversed
    {
        get => (bool)(GetValue(IsDirectionReversedProperty) ?? false);
        set => SetValue(IsDirectionReversedProperty, value);
    }

    /// <summary>Pixel inset at each track end.</summary>
    public double ReservedSpace
    {
        get => (double)(GetValue(ReservedSpaceProperty) ?? 0.0);
        set => SetValue(ReservedSpaceProperty, value);
    }

    /// <summary>Stock-template tick length across the bar (explicit sizes govern through the base clamp).</summary>
    protected override WpfSize MeasureCore(WpfSize available) => new(
        Placement is TickBarPlacement.Top or TickBarPlacement.Bottom ? 0 : 4,
        Placement is TickBarPlacement.Top or TickBarPlacement.Bottom ? 4 : 0);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var range = Maximum - Minimum;
        if (!(range > 0)) return;
        var horizontal = Placement is TickBarPlacement.Top or TickBarPlacement.Bottom;
        var track = (horizontal ? w : h) - 2 * Math.Max(0, ReservedSpace);
        if (track <= 0) return;
        var ink = BrushRender.ResolveFill(Fill, 0xFF000000u);
        foreach (var tick in TickPositions(range))
        {
            var fraction = (tick - Minimum) / range;
            if (IsDirectionReversed) fraction = 1 - fraction;
            fraction = Math.Clamp(fraction, 0, 1);
            var along = Math.Max(0, ReservedSpace) + fraction * track;
            if (horizontal)
                dc.DrawRectangle((float)(along - 0.5), 0, 1, (float)h, ink);
            else
                dc.DrawRectangle(0, (float)(along - 0.5), (float)w, 1, ink);
        }
    }

    private IEnumerable<double> TickPositions(double range)
    {
        if (Ticks is { Count: > 0 })
        {
            foreach (var tick in Ticks) yield return tick;
            yield break;
        }
        if (TickFrequency > 0)
        {
            for (var tick = Minimum; tick <= Maximum + range * 1e-9; tick += TickFrequency)
                yield return Math.Min(tick, Maximum);
        }
    }
}

/// <summary>
/// Font enumeration (matches System.Windows.Media.Fonts, static like the
/// original): system families/typefaces enumerate the live Skia font
/// manager (real platform fonts, never a canned list), and location
/// lookups enumerate real font files in the directory (null locations
/// throw ArgumentNullException, like WPF). Pack URIs stay a precise
/// boundary (no resource-font pipeline in the engine).
/// </summary>
public static class Fonts
{
    /// <summary>Installed families (matches Fonts.SystemFontFamilies).</summary>
    public static ICollection<FontFamily> SystemFontFamilies =>
        SKFontManager.Default.GetFontFamilies().Select(f => new FontFamily(f)).ToList();

    /// <summary>Installed faces (matches Fonts.SystemTypefaces).</summary>
    public static ICollection<Typeface> SystemTypefaces =>
        SKFontManager.Default.GetFontFamilies().Select(f => new Typeface(f)).ToList();

    /// <summary>Families in a font directory (matches Fonts.GetFontFamilies(string)).</summary>
    public static ICollection<FontFamily> GetFontFamilies(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return FontFilesIn(location).Select(f => f.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => new FontFamily(f)).ToList();
    }

    /// <summary>Families at a font location URI (matches Fonts.GetFontFamilies(Uri)).</summary>
    public static ICollection<FontFamily> GetFontFamilies(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        return GetFontFamilies(ResolveLocation(baseUri));
    }

    /// <summary>Families in a location relative to a base URI (matches Fonts.GetFontFamilies(Uri, string)).</summary>
    public static ICollection<FontFamily> GetFontFamilies(Uri baseUri, string location)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(location);
        return GetFontFamilies(System.IO.Path.Combine(ResolveLocation(baseUri), location));
    }

    /// <summary>Faces in a font directory (matches Fonts.GetTypefaces(string)).</summary>
    public static ICollection<Typeface> GetTypefaces(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return FontFilesIn(location).Select(ToTypeface).ToList();
    }

    /// <summary>Faces at a font location URI (matches Fonts.GetTypefaces(Uri)).</summary>
    public static ICollection<Typeface> GetTypefaces(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        return GetTypefaces(ResolveLocation(baseUri));
    }

    /// <summary>Faces in a location relative to a base URI (matches Fonts.GetTypefaces(Uri, string)).</summary>
    public static ICollection<Typeface> GetTypefaces(Uri baseUri, string location)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(location);
        return GetTypefaces(System.IO.Path.Combine(ResolveLocation(baseUri), location));
    }

    private static string ResolveLocation(Uri baseUri) =>
        baseUri.IsFile ? baseUri.LocalPath
        : throw new NotSupportedException($"Font location '{baseUri}' is not supported: only file URIs resolve (no resource-font pipeline).");

    private static IEnumerable<SKTypeface> FontFilesIn(string location)
    {
        if (!Directory.Exists(location)) return [];
        var faces = new List<SKTypeface>();
        foreach (var file in Directory.GetFiles(location).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var ext = System.IO.Path.GetExtension(file);
            if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase)) continue;
            var face = SKTypeface.FromFile(file);
            if (face is not null) faces.Add(face);
        }
        return faces;
    }

    private static Typeface ToTypeface(SKTypeface face) => new(
        new FontFamily(face.FamilyName),
        face.FontSlant switch
        {
            SKFontStyleSlant.Italic => FontStyles.Italic,
            SKFontStyleSlant.Oblique => FontStyles.Oblique,
            _ => FontStyles.Normal,
        },
        FontWeight.FromOpenTypeWeight(face.FontWeight),
        face.FontWidth switch
        {
            1 => FontStretches.UltraCondensed,
            2 => FontStretches.ExtraCondensed,
            3 => FontStretches.Condensed,
            4 => FontStretches.SemiCondensed,
            6 => FontStretches.SemiExpanded,
            7 => FontStretches.Expanded,
            8 => FontStretches.ExtraExpanded,
            9 => FontStretches.UltraExpanded,
            _ => FontStretches.Normal,
        });
}
