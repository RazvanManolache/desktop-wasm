namespace DesktopWasm.Wpf;

// RangeBase/Slider (WPF subset): gallery BasicInput/SliderPage and the
// UserDashboard age slider need real range behavior, not a name mapping.
// Value/min/max coerce like the original (value always lands inside the
// range, min never passes max), ValueChanged carries coerced old/new values,
// snap-to-tick rounds every set, the pointer drags the thumb with a grab
// offset (track presses step by LargeChange), and keyboard control follows
// focus. Retained limits: no Ticks collection (TickFrequency ticks only),
// no track-press auto-repeat, no selection range.

/// <summary>Tick mark placement (matches System.Windows.Controls.Primitives.TickPlacement).</summary>
public enum TickPlacement
{
    None = 0,
    TopLeft = 1,
    BottomRight = 2,
    Both = 3,
}

/// <summary>
/// Value-change payload (matches System.Windows.RoutedPropertyChangedEventArgs&lt;T&gt;).
/// Converted handlers read <see cref="OldValue"/> and <see cref="NewValue"/>.
/// </summary>
/// <typeparam name="T">Coerced value type (double for RangeBase).</typeparam>
public class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
{
    public RoutedPropertyChangedEventArgs(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>Coerced value before the change.</summary>
    public T OldValue { get; }

    /// <summary>Coerced value after the change.</summary>
    public T NewValue { get; }
}

/// <summary>Value-change handler shape (matches System.Windows.RoutedPropertyChangedEventHandler&lt;T&gt;).</summary>
public delegate void RoutedPropertyChangedEventHandler<T>(object sender, RoutedPropertyChangedEventArgs<T> e);

/// <summary>
/// Range base (matches System.Windows.Controls.Primitives.RangeBase, abstract
/// like the original). Coercion is explicit in the setters because the engine
/// dependency-property store has no coerce callbacks; the observable contract
/// matches WPF: Minimum never exceeds Maximum, Maximum never drops below
/// Minimum, and Value always lands inside the range after snapping.
/// </summary>
public abstract class RangeBase : Control
{
    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        "ValueChanged", RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeBase));

    /// <summary>Lower-bound identifier (matches System.Windows.Controls.Primitives.RangeBase.MinimumProperty).</summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register("Minimum", typeof(double), typeof(RangeBase), 0.0);

    /// <summary>Upper-bound identifier (matches System.Windows.Controls.Primitives.RangeBase.MaximumProperty).</summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register("Maximum", typeof(double), typeof(RangeBase), 1.0);

    /// <summary>Current-value identifier (matches System.Windows.Controls.Primitives.RangeBase.ValueProperty).</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(double), typeof(RangeBase), 0.0);

    /// <summary>Keyboard/arrow-step identifier (matches System.Windows.Controls.Primitives.RangeBase.SmallChangeProperty).</summary>
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register("SmallChange", typeof(double), typeof(RangeBase), 0.1);

    /// <summary>Track-press/page-step identifier (matches System.Windows.Controls.Primitives.RangeBase.LargeChangeProperty).</summary>
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register("LargeChange", typeof(double), typeof(RangeBase), 1.0);

    /// <summary>Lower bound (coerced down to Maximum; coerces Value into range).</summary>
    public double Minimum
    {
        get => (double?)GetValue(MinimumProperty) ?? 0.0;
        set
        {
            var coerced = Math.Min(value, Maximum);
            if (Minimum == coerced) return;
            var old = Minimum;
            SetValue(MinimumProperty, coerced);
            OnMinimumChanged(old, coerced);
            Value = Value; // re-coerce into the new range (raises ValueChanged when clamped)
            InvalidateMeasure();
        }
    }

    /// <summary>Upper bound (coerced up to Minimum; coerces Value into range).</summary>
    public double Maximum
    {
        get => (double?)GetValue(MaximumProperty) ?? 1.0;
        set
        {
            var coerced = Math.Max(value, Minimum);
            if (Maximum == coerced) return;
            var old = Maximum;
            SetValue(MaximumProperty, coerced);
            OnMaximumChanged(old, coerced);
            Value = Value;
            InvalidateMeasure();
        }
    }

    /// <summary>Current value (clamped to the range, snapped when enabled).</summary>
    public double Value
    {
        get => (double?)GetValue(ValueProperty) ?? 0.0;
        set
        {
            var coerced = CoerceValue(value);
            if (Value == coerced) return;
            var old = Value;
            SetValue(ValueProperty, coerced);
            InvalidateVisual();
            OnValueChanged(old, coerced);
            UpdateSources(); // TwoWay bindings (gallery sliders) push on edits
        }
    }

    /// <summary>Keyboard/arrow step.</summary>
    public double SmallChange
    {
        get => (double?)GetValue(SmallChangeProperty) ?? 0.1;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>Track-press/page step.</summary>
    public double LargeChange
    {
        get => (double?)GetValue(LargeChangeProperty) ?? 1.0;
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>Raised after Value actually changes (never for equal coerced sets).</summary>
    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;

    /// <summary>Clamps into [Minimum, Maximum], then snaps to the tick grid.</summary>
    protected double CoerceValue(double value)
    {
        var clamped = Math.Clamp(value, Minimum, Maximum);
        return SnapToTick(clamped);
    }

    /// <summary>Rounds into the Minimum-anchored TickFrequency grid when snapping is on.</summary>
    protected double SnapToTick(double value)
    {
        if (!IsSnapToTickEnabled || TickFrequency <= 0 || Maximum <= Minimum) return value;
        var steps = Math.Round((value - Minimum) / TickFrequency);
        return Math.Clamp(Minimum + steps * TickFrequency, Minimum, Maximum);
    }

    /// <summary>Bindable tick spacing (ticks paint from this; no Ticks collection in v1).</summary>
    public virtual double TickFrequency { get; set; } = 1.0;

    /// <summary>Rounds sets to the tick grid when true (matches WPF: every set snaps).</summary>
    public virtual bool IsSnapToTickEnabled { get; set; }

    /// <summary>Selection-range display (matches Slider.IsSelectionRangeEnabledProperty; false default like WPF).</summary>
    public static readonly DependencyProperty IsSelectionRangeEnabledProperty =
        DependencyProperty.Register("IsSelectionRangeEnabled", typeof(bool), typeof(Slider),
            new FrameworkPropertyMetadata(false));

    /// <summary>Selection-range start (matches Slider.SelectionStartProperty).</summary>
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register("SelectionStart", typeof(double), typeof(Slider),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Selection-range end (matches Slider.SelectionEndProperty).</summary>
    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register("SelectionEnd", typeof(double), typeof(Slider),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Explicit ticks (matches Slider.TicksProperty; null means none).</summary>
    public static readonly DependencyProperty TicksProperty =
        DependencyProperty.Register("Ticks", typeof(DoubleCollection), typeof(Slider),
            new FrameworkPropertyMetadata(null));

    /// <summary>Selection-range display.</summary>
    public bool IsSelectionRangeEnabled
    {
        get => (bool)(GetValue(IsSelectionRangeEnabledProperty) ?? false);
        set => SetValue(IsSelectionRangeEnabledProperty, value);
    }

    /// <summary>Selection-range start.</summary>
    public double SelectionStart
    {
        get => (double)(GetValue(SelectionStartProperty) ?? 0.0);
        set => SetValue(SelectionStartProperty, value);
    }

    /// <summary>Selection-range end.</summary>
    public double SelectionEnd
    {
        get => (double)(GetValue(SelectionEndProperty) ?? 0.0);
        set => SetValue(SelectionEndProperty, value);
    }

    /// <summary>Explicit ticks (null means none).</summary>
    public DoubleCollection? Ticks
    {
        get => (DoubleCollection?)GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    /// <summary>Jump-to-click (matches Slider.IsMoveToPointEnabledProperty; false default like WPF).</summary>
    public static readonly DependencyProperty IsMoveToPointEnabledProperty =
        DependencyProperty.Register("IsMoveToPointEnabled", typeof(bool), typeof(Slider),
            new FrameworkPropertyMetadata(false));

    /// <summary>Jump-to-click.</summary>
    public bool IsMoveToPointEnabled
    {
        get => (bool)(GetValue(IsMoveToPointEnabledProperty) ?? false);
        set => SetValue(IsMoveToPointEnabledProperty, value);
    }

    /// <summary>Reports range-limit edits (matches WPF virtuals).</summary>
    protected virtual void OnMinimumChanged(double oldMinimum, double newMinimum) { }

    /// <summary>Reports range-limit edits (matches WPF virtuals).</summary>
    protected virtual void OnMaximumChanged(double oldMaximum, double newMaximum) { }

    /// <summary>Raises ValueChanged with coerced old/new values.</summary>
    protected virtual void OnValueChanged(double oldValue, double newValue)
    {
        var args = new RoutedPropertyChangedEventArgs<double>(oldValue, newValue)
        {
            RoutedEvent = ValueChangedEvent,
            Source = this,
            OriginalSource = this,
        };
        ValueChanged?.Invoke(this, args);
    }
}

/// <summary>
/// Slider (matches System.Windows.Controls.Slider): horizontal/vertical
/// track with a draggable thumb, tick marks, pointer capture drag and
/// keyboard control. Paints through retained drawing commands on both
/// renderers (rectangles + tick lines only; no template inflation).
/// </summary>
public class Slider : RangeBase
{
    /// <summary>Large-decrease command (matches Slider.DecreaseLarge).</summary>
    public static RoutedCommand DecreaseLarge { get; } = new(nameof(DecreaseLarge), typeof(Slider));

    /// <summary>Small-decrease command (matches Slider.DecreaseSmall).</summary>
    public static RoutedCommand DecreaseSmall { get; } = new(nameof(DecreaseSmall), typeof(Slider));

    /// <summary>Large-increase command (matches Slider.IncreaseLarge).</summary>
    public static RoutedCommand IncreaseLarge { get; } = new(nameof(IncreaseLarge), typeof(Slider));

    /// <summary>Small-increase command (matches Slider.IncreaseSmall).</summary>
    public static RoutedCommand IncreaseSmall { get; } = new(nameof(IncreaseSmall), typeof(Slider));

    /// <summary>Minimize command (matches Slider.MinimizeValue).</summary>
    public static RoutedCommand MinimizeValue { get; } = new(nameof(MinimizeValue), typeof(Slider));

    /// <summary>Maximize command (matches Slider.MaximizeValue).</summary>
    public static RoutedCommand MaximizeValue { get; } = new(nameof(MaximizeValue), typeof(Slider));

    private const double TrackThickness = 4;
    private const double ThumbSize = 12;
    private const double TickLength = 5;

    private bool _dragging;
    private double _grabDelta;
    private bool _focused;

    public override bool Focusable => true;

    /// <summary>Track direction (default Horizontal, like WPF).</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Swaps min/max ends (arrows, track steps and paint follow).</summary>
    public bool IsDirectionReversed { get; set; }

    /// <summary>Where tick marks paint (None hides them; matches the WPF default).</summary>
    public TickPlacement TickPlacement { get; set; } = TickPlacement.None;

    private AutoToolTipPlacement _autoToolTipPlacement = AutoToolTipPlacement.None;

    private int _autoToolTipPrecision;

    /// <summary>
    /// Auto-tooltip placement knob (matches
    /// System.Windows.Controls.Slider.AutoToolTipPlacement, WPF default None).
    /// Retained knob: the engine renders no auto-tooltip popup in v1; values
    /// outside the defined enum throw ArgumentException like the framework
    /// ValidateValueCallback.
    /// </summary>
    public AutoToolTipPlacement AutoToolTipPlacement
    {
        get => _autoToolTipPlacement;
        set => _autoToolTipPlacement = value is AutoToolTipPlacement.None or AutoToolTipPlacement.TopLeft or AutoToolTipPlacement.BottomRight
            ? value
            : throw new ArgumentException($"Invalid {nameof(AutoToolTipPlacement)} value '{(int)value}'.", nameof(value));
    }

    /// <summary>
    /// Auto-tooltip precision knob (matches
    /// System.Windows.Controls.Slider.AutoToolTipPrecision, WPF default 0).
    /// Retained knob like <see cref="AutoToolTipPlacement"/>; negative values
    /// throw ArgumentException like the framework ValidateValueCallback.
    /// </summary>
    public int AutoToolTipPrecision
    {
        get => _autoToolTipPrecision;
        set => _autoToolTipPrecision = value >= 0
            ? value
            : throw new ArgumentException($"{nameof(AutoToolTipPrecision)} must be non-negative.", nameof(value));
    }

    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; _dragging = false; InvalidateVisual(); }

    /// <summary>Usable track span inside the arranged rect.</summary>
    internal WpfRect TrackRect()
    {
        var r = RenderRect;
        if (Orientation == Orientation.Horizontal)
        {
            var y = r.Y + (r.Height - TrackThickness) / 2;
            return new WpfRect(r.X + ThumbSize / 2, y, Math.Max(0, r.Width - ThumbSize), TrackThickness);
        }
        var x = r.X + (r.Width - TrackThickness) / 2;
        return new WpfRect(x, r.Y + ThumbSize / 2, TrackThickness, Math.Max(0, r.Height - ThumbSize));
    }

    /// <summary>Thumb square centered on the current value.</summary>
    internal WpfRect ThumbRect()
    {
        var track = TrackRect();
        var t = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        if (IsDirectionReversed) t = 1 - t;
        if (Orientation == Orientation.Horizontal)
        {
            var cx = track.X + t * track.Width;
            return new WpfRect(cx - ThumbSize / 2, track.Y + (track.Height - ThumbSize) / 2, ThumbSize, ThumbSize);
        }
        var cy = track.Y + track.Height - t * track.Height;
        return new WpfRect(track.X + (track.Width - ThumbSize) / 2, cy - ThumbSize / 2, ThumbSize, ThumbSize);
    }

    /// <summary>Range value under a point (grab math shares this with the drag path).</summary>
    internal double ValueFromPoint(WpfPoint p)
    {
        var track = TrackRect();
        double t = Orientation == Orientation.Horizontal
            ? (track.Width <= 0 ? 0 : (p.X - track.X) / track.Width)
            : (track.Height <= 0 ? 0 : (track.Y + track.Height - p.Y) / track.Height);
        if (IsDirectionReversed) t = 1 - t;
        return Minimum + t * (Maximum - Minimum);
    }

    private static bool InRect(WpfPoint p, WpfRect r) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
            {
                // Press takes focus; the thumb drags with its grab offset while
                // a track press steps one LargeChange toward the click (the
                // press-and-hold auto-repeat of RepeatButton is a retained limit).
                Focus();
                var p = new WpfPoint(pd.Args.X, pd.Args.Y);
                if (InRect(p, ThumbRect()))
                {
                    _dragging = true;
                    _grabDelta = ValueFromPoint(p) - Value;
                }
                else if (InRect(p, RenderRect))
                {
                    var target = ValueFromPoint(p);
                    Value = target > Value
                        ? Math.Min(target, Value + LargeChange)
                        : Math.Max(target, Value - LargeChange);
                    _dragging = true;
                    _grabDelta = ValueFromPoint(p) - Value;
                }
                e.Handled = true;
                break;
            }
            case Core.WasmInputEvent.Pointer pm when pm.Args.Kind == Core.WasmPointerKind.Move:
                if (_dragging)
                {
                    Value = ValueFromPoint(new WpfPoint(pm.Args.X, pm.Args.Y)) - _grabDelta;
                    e.Handled = true;
                }
                break;
            case Core.WasmInputEvent.Pointer pu when pu.Args.Kind is Core.WasmPointerKind.Up or Core.WasmPointerKind.Cancel:
                _dragging = false;
                break;
            case Core.WasmInputEvent.Key k when k.Args.IsDown && _focused:
            {
                var dir = IsDirectionReversed ? -1.0 : 1.0;
                switch (k.Args.Key)
                {
                    case "ArrowRight":
                    case "ArrowUp": Value += dir * SmallChange; e.Handled = true; break;
                    case "ArrowLeft":
                    case "ArrowDown": Value -= dir * SmallChange; e.Handled = true; break;
                    case "PageUp": Value += dir * LargeChange; e.Handled = true; break;
                    case "PageDown": Value -= dir * LargeChange; e.Handled = true; break;
                    case "Home": Value = dir > 0 ? Minimum : Maximum; e.Handled = true; break;
                    case "End": Value = dir > 0 ? Maximum : Minimum; e.Handled = true; break;
                }
                break;
            }
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var ticks = TickPlacement != TickPlacement.None ? TickLength + 4 : 0;
        if (Orientation == Orientation.Horizontal)
            return new WpfSize(
                double.IsNaN(Width) ? Math.Min(available.Width, 200) : Width,
                double.IsNaN(Height) ? 22 + ticks : Height);
        return new WpfSize(
            double.IsNaN(Width) ? 22 + ticks : Width,
            double.IsNaN(Height) ? Math.Min(available.Height, 200) : Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var enabled = IsEffectivelyEnabled;
        var track = TrackRect();
        var trackRgba = enabled ? 0xD8D8D8FFu : 0xCCCCCCFFu;
        dc.DrawRectangle((float)track.X, (float)track.Y, (float)track.Width, (float)track.Height, trackRgba);
        PaintTicks(dc, track, enabled);
        var thumb = ThumbRect();
        var thumbRgba = !enabled ? 0xCCCCCCFFu : _dragging ? 0x707070FFu : 0xFFFFFFFFu;
        var edgeRgba = enabled ? 0x707070FFu : 0x909090FFu;
        dc.DrawRectangle((float)thumb.X, (float)thumb.Y, (float)thumb.Width, (float)thumb.Height, edgeRgba);
        if (thumb.Width > 2 && thumb.Height > 2)
            dc.DrawRectangle((float)thumb.X + 1, (float)thumb.Y + 1,
                (float)thumb.Width - 2, (float)thumb.Height - 2, thumbRgba);
        if (_focused && enabled)
        {
            // Focus ring is a 1px outline (DrawRectangle fills; compose the edges).
            const uint ring = 0x285B45FF;
            var fx = (float)r.X;
            var fy = (float)r.Y;
            var fw = (float)r.Width;
            var fh = (float)r.Height;
            dc.DrawRectangle(fx, fy, fw, 1, ring);
            dc.DrawRectangle(fx, fy + fh - 1, fw, 1, ring);
            dc.DrawRectangle(fx, fy + 1, 1, Math.Max(0, fh - 2), ring);
            dc.DrawRectangle(fx + fw - 1, fy + 1, 1, Math.Max(0, fh - 2), ring);
        }
    }

    private void PaintTicks(DrawingContext dc, WpfRect track, bool enabled)
    {
        if (TickPlacement == TickPlacement.None || TickFrequency <= 0 || Maximum <= Minimum) return;
        var ink = enabled ? 0x909090FFu : 0xB0B0B0FFu;
        for (var v = Minimum; v <= Maximum + TickFrequency / 2; v += TickFrequency)
        {
            var t = (Math.Min(v, Maximum) - Minimum) / (Maximum - Minimum);
            if (IsDirectionReversed) t = 1 - t;
            if (Orientation == Orientation.Horizontal)
            {
                var x = (float)(track.X + t * track.Width);
                if (TickPlacement is TickPlacement.TopLeft or TickPlacement.Both)
                    dc.DrawLine(x, (float)track.Y - (float)TickLength, x, (float)track.Y, 1, ink);
                if (TickPlacement is TickPlacement.BottomRight or TickPlacement.Both)
                    dc.DrawLine(x, (float)(track.Y + track.Height), x, (float)(track.Y + track.Height + TickLength), 1, ink);
            }
            else
            {
                var y = (float)(track.Y + track.Height - t * track.Height);
                if (TickPlacement is TickPlacement.TopLeft or TickPlacement.Both)
                    dc.DrawLine((float)track.X - (float)TickLength, y, (float)track.X, y, 1, ink);
                if (TickPlacement is TickPlacement.BottomRight or TickPlacement.Both)
                    dc.DrawLine((float)(track.X + track.Width), y, (float)(track.X + track.Width + TickLength), y, 1, ink);
            }
        }
    }
}
