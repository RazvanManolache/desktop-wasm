namespace DesktopWasm.Wpf;

// Animation keyframes and timeline groups (PresentationCore subset, live
// shapes verified against the 9.0.21 PresentationCore ref pack plus a live
// net9.0-windows runtime probe): Animatable as the real Freezable-derived
// animation host (Brush, Timeline, GeneralTransform, Geometry and Effect
// rebase onto it so converted Animatable::BeginAnimation/Clone calls bind),
// the double key-frame family with real interpolation, timeline groups with
// the Storyboard rebase, and sine/cubic easings.
//
// Retained limits: Storyboard children still run DoubleAnimation only (a
// DoubleAnimationUsingKeyFrames child fails fast like before; the key-frame
// animation itself evaluates through BeginAnimation); IsCumulative needs
// repeat-index plumbing the deterministic clock does not carry; the engine
// EasingFunctionBase default stays EaseIn while live WPF defaults to
// EaseOut (pre-existing engine contract pinned by PowerEase_raises_to_power;
// the new easings inherit it, so converted transitions ease EaseIn here).

/// <summary>
/// Animation host (matches System.Windows.Media.Animation.Animatable):
/// Freezable-derived base for brushes, timelines, transforms, geometries
/// and effects. BeginAnimation forwards to the deterministic pump; a null
/// timeline removes the animation.
/// </summary>
public abstract class Animatable : Freezable
{
    /// <summary>Starts (or, with null, removes) a property animation.</summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation) =>
        WpfAnimationClock.BeginAnimation(this, dp, animation, HandoffBehavior.SnapshotAndReplace);

    /// <summary>Starts (or removes) a property animation with a handoff rule.</summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior) =>
        WpfAnimationClock.BeginAnimation(this, dp, animation, handoffBehavior);

    /// <summary>Modifiable copy typed as Animatable (matches Animatable.Clone).</summary>
    public new Animatable Clone() => (Animatable)base.Clone();
}

/// <summary>Key-frame contract (matches System.Windows.Media.Animation.IKeyFrame).</summary>
public interface IKeyFrame
{
    /// <summary>Frame value (boxed double for the double family).</summary>
    object? Value { get; set; }

    /// <summary>When the frame takes effect.</summary>
    KeyTime KeyTime { get; set; }
}

/// <summary>Deferred key-time contract (template StaticResource KeyTime
/// attributes resolve at storyboard run, like other template deferrals).</summary>
internal interface IKeyTimeDeferred
{
    /// <summary>When the frame takes effect.</summary>
    KeyTime KeyTime { get; set; }

    /// <summary>StaticResource key for the KeyTime (null when parsed).</summary>
    string? DeferredKeyTimeKey { get; set; }
}

/// <summary>Double key-frame base (matches DoubleKeyFrame : Freezable).</summary>
public abstract class DoubleKeyFrame : Freezable, IKeyFrame, IKeyTimeDeferred
{
    /// <summary>Identifies the KeyTime store.</summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register("KeyTime", typeof(KeyTime), typeof(DoubleKeyFrame), KeyTime.Uniform);

    /// <summary>Identifies the Value store.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(double), typeof(DoubleKeyFrame), 0.0);

    /// <summary>Empty frame (Uniform time, zero value).</summary>
    protected DoubleKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value (live-verified shape).</summary>
    protected DoubleKeyFrame(double value) => Value = value;

    /// <summary>Timed frame carrying a value (live-verified shape).</summary>
    protected DoubleKeyFrame(double value, KeyTime keyTime)
    {
        Value = value;
        KeyTime = keyTime;
    }

    /// <summary>When the frame takes effect (default Uniform).</summary>
    public KeyTime KeyTime
    {
        get => (KeyTime)GetValue(KeyTimeProperty)!;
        set => SetValue(KeyTimeProperty, value);
    }

    /// <summary>StaticResource key for the KeyTime (template visual states).</summary>
    public string? DeferredKeyTimeKey { get; set; }

    /// <summary>Value applied once the key time passes.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    object? IKeyFrame.Value
    {
        get => Value;
        set => Value = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            null => 0.0,
            _ => throw new ArgumentException($"Cannot convert '{value.GetType().Name}' to double.", nameof(value)),
        };
    }
}

/// <summary>Jump-cut double frame (matches DiscreteDoubleKeyFrame).</summary>
public sealed class DiscreteDoubleKeyFrame : DoubleKeyFrame
{
    /// <summary>Empty frame.</summary>
    public DiscreteDoubleKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public DiscreteDoubleKeyFrame(double value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscreteDoubleKeyFrame(double value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Linear double frame (matches LinearDoubleKeyFrame).</summary>
public sealed class LinearDoubleKeyFrame : DoubleKeyFrame
{
    /// <summary>Empty frame.</summary>
    public LinearDoubleKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public LinearDoubleKeyFrame(double value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public LinearDoubleKeyFrame(double value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Eased double frame (matches EasingDoubleKeyFrame).</summary>
public sealed class EasingDoubleKeyFrame : DoubleKeyFrame
{
    /// <summary>Empty frame.</summary>
    public EasingDoubleKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public EasingDoubleKeyFrame(double value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public EasingDoubleKeyFrame(double value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed frame with an easing curve (live-verified shape).</summary>
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction? easingFunction)
        : base(value, keyTime) => EasingFunction = easingFunction;

    /// <summary>Easing curve for the segment ending at this frame (null = linear).</summary>
    public IEasingFunction? EasingFunction { get; set; }
}

/// <summary>Spline-eased double frame (matches SplineDoubleKeyFrame;
/// the curve reuses the slice-81 KeySpline with its ported Newton solve).</summary>
public sealed class SplineDoubleKeyFrame : DoubleKeyFrame
{
    /// <summary>Empty frame (linear spline, the verified native default).</summary>
    public SplineDoubleKeyFrame()
    {
    }

    /// <summary>Spline frame carrying a value.</summary>
    public SplineDoubleKeyFrame(double value) : base(value)
    {
    }

    /// <summary>Timed spline frame carrying a value.</summary>
    public SplineDoubleKeyFrame(double value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed spline frame with a curve.</summary>
    public SplineDoubleKeyFrame(double value, KeyTime keyTime, KeySpline? keySpline) : base(value, keyTime) =>
        KeySpline = keySpline ?? new KeySpline();

    /// <summary>Bezier curve for the segment ending at this frame (never null, like native).</summary>
    public KeySpline KeySpline { get; set; } = new KeySpline();
}

/// <summary>Double frame list (matches DoubleKeyFrameCollection).</summary>
public sealed class DoubleKeyFrameCollection : System.Collections.ObjectModel.Collection<DoubleKeyFrame>
{
}

/// <summary>
 /// Key-framed double animation (matches DoubleAnimationUsingKeyFrames):
/// interpolates through resolved key times. Empty holds the origin;
/// before the first key holds the origin (the engine key-frame contract,
/// shared with ObjectAnimationUsingKeyFrames); past the last holds its
/// value. Segments interpolate per the ending frame: discrete jumps at
/// the frame time, linear lerps, eased applies that frame's function
/// (null function = linear). IsAdditive offsets from the origin value;
/// IsCumulative is retained (no repeat-index plumbing in the pump).
/// </summary>
public class DoubleAnimationUsingKeyFrames : AnimationTimeline, IKeyFrameAnimation
{
    /// <summary>Frames in collection order.</summary>
    public DoubleKeyFrameCollection KeyFrames { get; } = new();

    /// <inheritdoc />
    System.Collections.IList IKeyFrameAnimation.KeyFrames
    {
        get => KeyFrames;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var frames = new List<DoubleKeyFrame>(value.Count);
            foreach (var item in value)
                frames.Add(item as DoubleKeyFrame ??
                    throw new ArgumentException($"KeyFrames accepts DoubleKeyFrame items, not '{item?.GetType().Name ?? "<null>"}'.", nameof(value)));
            KeyFrames.Clear();
            foreach (var frame in frames) KeyFrames.Add(frame);
        }
    }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(double);

    /// <summary>
    /// Automatic-duration resolution: the latest absolute key time, or a
    /// 1-second fallback when every key is Uniform/Percent (shared with
    /// ObjectAnimationUsingKeyFrames.GetNaturalDuration).
    /// </summary>
    internal TimeSpan GetNaturalDuration()
    {
        if (KeyFrames.Count == 0) return TimeSpan.Zero;
        var latest = TimeSpan.Zero;
        var anyAbsolute = false;
        foreach (var frame in KeyFrames)
        {
            if (frame.KeyTime.Type != KeyTimeType.TimeSpan) continue;
            anyAbsolute = true;
            if (frame.KeyTime.Time > latest) latest = frame.KeyTime.Time;
        }
        return anyAbsolute ? latest : TimeSpan.FromSeconds(1);
    }

    /// <summary>Typed sample at the clock position.</summary>
    public double GetCurrentValue(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var value = Sample(defaultOriginValue, defaultDestinationValue, animationClock);
        return IsAdditive ? defaultOriginValue + value : value;
    }

    private double Sample(double origin, double destination, AnimationClock clock)
    {
        if (KeyFrames.Count == 0) return origin;
        var duration = clock.IterationDuration;
        var ordered = new List<(TimeSpan At, DoubleKeyFrame Frame)>(KeyFrames.Count);
        for (var i = 0; i < KeyFrames.Count; i++)
            ordered.Add((KeyFrames[i].KeyTime.Resolve(duration, i, KeyFrames.Count), KeyFrames[i]));
        ordered.Sort(static (a, b) => a.At.CompareTo(b.At));
        if (clock.CurrentTime < ordered[0].At) return origin;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (clock.CurrentTime < ordered[i].At)
                return Interpolate(ordered[i - 1], ordered[i], clock.CurrentTime);
        }
        return ordered[^1].Frame.Value;
    }

    private static double Interpolate((TimeSpan At, DoubleKeyFrame Frame) from, (TimeSpan At, DoubleKeyFrame Frame) to, TimeSpan at)
    {
        if (to.At <= from.At) return to.Frame.Value;
        var progress = (at - from.At).Ticks / (double)(to.At - from.At).Ticks;
        progress = double.IsNaN(progress) ? 0.0 : Math.Clamp(progress, 0.0, 1.0);
        return to.Frame switch
        {
            DiscreteDoubleKeyFrame => progress >= 1.0 ? to.Frame.Value : from.Frame.Value,
            SplineDoubleKeyFrame spline => from.Frame.Value +
                ((to.Frame.Value - from.Frame.Value) * spline.KeySpline.GetSplineProgress(progress)),
            EasingDoubleKeyFrame easing => from.Frame.Value +
                ((to.Frame.Value - from.Frame.Value) * (easing.EasingFunction?.Ease(progress) ?? progress)),
            _ => from.Frame.Value + ((to.Frame.Value - from.Frame.Value) * progress),
        };
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToDouble(defaultOriginValue), ToDouble(defaultDestinationValue), animationClock);

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        _ => 0.0,
    };
}

/// <summary>Point key-frame base (matches PointKeyFrame : Freezable).</summary>
public abstract class PointKeyFrame : Freezable, IKeyFrame, IKeyTimeDeferred
{
    /// <summary>Identifies the KeyTime store.</summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register("KeyTime", typeof(KeyTime), typeof(PointKeyFrame), KeyTime.Uniform);

    /// <summary>Identifies the Value store.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(Point), typeof(PointKeyFrame), new Point());

    /// <summary>Empty frame (Uniform time, origin value).</summary>
    protected PointKeyFrame()
    {
    }

    /// <summary>Frame carrying a value (live-verified shape).</summary>
    protected PointKeyFrame(Point value) => Value = value;

    /// <summary>Timed frame carrying a value (live-verified shape).</summary>
    protected PointKeyFrame(Point value, KeyTime keyTime)
    {
        Value = value;
        KeyTime = keyTime;
    }

    /// <summary>When the frame takes effect (default Uniform).</summary>
    public KeyTime KeyTime
    {
        get => (KeyTime)GetValue(KeyTimeProperty)!;
        set => SetValue(KeyTimeProperty, value);
    }

    /// <summary>StaticResource key for the KeyTime (template visual states).</summary>
    public string? DeferredKeyTimeKey { get; set; }

    /// <summary>Value applied once the key time passes.</summary>
    public Point Value
    {
        get => (Point)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    object? IKeyFrame.Value
    {
        get => Value;
        set => Value = value switch
        {
            Point p => p,
            null => new Point(),
            _ => throw new ArgumentException($"Cannot convert '{value.GetType().Name}' to Point.", nameof(value)),
        };
    }
}

/// <summary>Jump-cut point frame (matches DiscretePointKeyFrame).</summary>
public sealed class DiscretePointKeyFrame : PointKeyFrame
{
    /// <summary>Empty frame.</summary>
    public DiscretePointKeyFrame()
    {
    }

    /// <summary>Frame carrying a value.</summary>
    public DiscretePointKeyFrame(Point value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscretePointKeyFrame(Point value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Linear point frame (matches LinearPointKeyFrame).</summary>
public sealed class LinearPointKeyFrame : PointKeyFrame
{
    /// <summary>Empty frame.</summary>
    public LinearPointKeyFrame()
    {
    }

    /// <summary>Frame carrying a value.</summary>
    public LinearPointKeyFrame(Point value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public LinearPointKeyFrame(Point value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Spline-eased point frame (matches SplinePointKeyFrame).</summary>
public sealed class SplinePointKeyFrame : PointKeyFrame
{
    /// <summary>Empty frame (linear spline, the verified native default).</summary>
    public SplinePointKeyFrame()
    {
    }

    /// <summary>Spline frame carrying a value.</summary>
    public SplinePointKeyFrame(Point value) : base(value)
    {
    }

    /// <summary>Timed spline frame carrying a value.</summary>
    public SplinePointKeyFrame(Point value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed spline frame with a curve.</summary>
    public SplinePointKeyFrame(Point value, KeyTime keyTime, KeySpline? keySpline) : base(value, keyTime) =>
        KeySpline = keySpline ?? new KeySpline();

    /// <summary>Bezier curve for the segment ending at this frame (never null, like native).</summary>
    public KeySpline KeySpline { get; set; } = new KeySpline();
}

/// <summary>Eased point frame (matches EasingPointKeyFrame).</summary>
public sealed class EasingPointKeyFrame : PointKeyFrame
{
    /// <summary>Empty frame.</summary>
    public EasingPointKeyFrame()
    {
    }

    /// <summary>Frame carrying a value.</summary>
    public EasingPointKeyFrame(Point value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public EasingPointKeyFrame(Point value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed frame with an easing curve (live-verified shape).</summary>
    public EasingPointKeyFrame(Point value, KeyTime keyTime, IEasingFunction? easingFunction)
        : base(value, keyTime) => EasingFunction = easingFunction;

    /// <summary>Easing curve for the segment ending at this frame (null = linear).</summary>
    public IEasingFunction? EasingFunction { get; set; }
}

/// <summary>Point frame list (matches PointKeyFrameCollection).</summary>
public sealed class PointKeyFrameCollection : System.Collections.ObjectModel.Collection<PointKeyFrame>
{
}

/// <summary>
/// Key-framed point animation (matches PointAnimationUsingKeyFrames):
/// interpolates X/Y through resolved key times. Semantics mirror the double
/// family: empty holds the origin; segments interpolate per the ending
/// frame (discrete jumps, linear lerps, spline eases, eased applies that
/// frame's function); IsAdditive offsets the origin component-wise.
/// </summary>
public class PointAnimationUsingKeyFrames : AnimationTimeline, IKeyFrameAnimation
{
    /// <summary>Frames in collection order.</summary>
    public PointKeyFrameCollection KeyFrames { get; } = new();

    /// <inheritdoc />
    System.Collections.IList IKeyFrameAnimation.KeyFrames
    {
        get => KeyFrames;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var frames = new List<PointKeyFrame>(value.Count);
            foreach (var item in value)
                frames.Add(item as PointKeyFrame ??
                    throw new ArgumentException($"KeyFrames accepts PointKeyFrame items, not '{item?.GetType().Name ?? "<null>"}'.", nameof(value)));
            KeyFrames.Clear();
            foreach (var frame in frames) KeyFrames.Add(frame);
        }
    }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Point);

    /// <summary>
    /// Automatic-duration resolution: the latest absolute key time, or a
    /// 1-second fallback when every key is Uniform/Percent (shared with
    /// the double family).
    /// </summary>
    internal TimeSpan GetNaturalDuration()
    {
        if (KeyFrames.Count == 0) return TimeSpan.Zero;
        var latest = TimeSpan.Zero;
        var anyAbsolute = false;
        foreach (var frame in KeyFrames)
        {
            if (frame.KeyTime.Type != KeyTimeType.TimeSpan) continue;
            anyAbsolute = true;
            if (frame.KeyTime.Time > latest) latest = frame.KeyTime.Time;
        }
        return anyAbsolute ? latest : TimeSpan.FromSeconds(1);
    }

    /// <summary>Typed sample at the clock position.</summary>
    public Point GetCurrentValue(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var value = Sample(defaultOriginValue, defaultDestinationValue, animationClock);
        return IsAdditive ? new Point(defaultOriginValue.X + value.X, defaultOriginValue.Y + value.Y) : value;
    }

    private Point Sample(Point origin, Point destination, AnimationClock clock)
    {
        if (KeyFrames.Count == 0) return origin;
        var duration = clock.IterationDuration;
        var ordered = new List<(TimeSpan At, PointKeyFrame Frame)>(KeyFrames.Count);
        for (var i = 0; i < KeyFrames.Count; i++)
            ordered.Add((KeyFrames[i].KeyTime.Resolve(duration, i, KeyFrames.Count), KeyFrames[i]));
        ordered.Sort(static (a, b) => a.At.CompareTo(b.At));
        if (clock.CurrentTime < ordered[0].At) return origin;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (clock.CurrentTime < ordered[i].At)
                return Interpolate(ordered[i - 1], ordered[i], clock.CurrentTime);
        }
        return ordered[^1].Frame.Value;
    }

    private static Point Interpolate((TimeSpan At, PointKeyFrame Frame) from, (TimeSpan At, PointKeyFrame Frame) to, TimeSpan at)
    {
        if (to.At <= from.At) return to.Frame.Value;
        var progress = (at - from.At).Ticks / (double)(to.At - from.At).Ticks;
        progress = double.IsNaN(progress) ? 0.0 : Math.Clamp(progress, 0.0, 1.0);
        return to.Frame switch
        {
            DiscretePointKeyFrame => progress >= 1.0 ? to.Frame.Value : from.Frame.Value,
            SplinePointKeyFrame spline => Lerp(from.Frame.Value, to.Frame.Value, spline.KeySpline.GetSplineProgress(progress)),
            EasingPointKeyFrame easing => Lerp(from.Frame.Value, to.Frame.Value, easing.EasingFunction?.Ease(progress) ?? progress),
            _ => Lerp(from.Frame.Value, to.Frame.Value, progress),
        };
    }

    private static Point Lerp(Point from, Point to, double progress) =>
        new(from.X + ((to.X - from.X) * progress), from.Y + ((to.Y - from.Y) * progress));

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToPoint(defaultOriginValue), ToPoint(defaultDestinationValue), animationClock);

    private static Point ToPoint(object? value) => value switch
    {
        Point p => p,
        Vector v => new Point(v.X, v.Y),
        _ => new Point(),
    };
}

/// <summary>Boolean key-frame base (matches BooleanKeyFrame : Freezable; abstract like WPF).</summary>
public abstract class BooleanKeyFrame : Freezable, IKeyFrame, IKeyTimeDeferred
{
    /// <summary>Identifies the KeyTime store.</summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register("KeyTime", typeof(KeyTime), typeof(BooleanKeyFrame), KeyTime.Uniform);

    /// <summary>Identifies the Value store.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(bool), typeof(BooleanKeyFrame), false);

    /// <summary>Empty frame (Uniform time, false value).</summary>
    protected BooleanKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    protected BooleanKeyFrame(bool value) => Value = value;

    /// <summary>Timed frame carrying a value.</summary>
    protected BooleanKeyFrame(bool value, KeyTime keyTime)
    {
        Value = value;
        KeyTime = keyTime;
    }

    /// <summary>When the frame takes effect (default Uniform).</summary>
    public KeyTime KeyTime
    {
        get => (KeyTime)GetValue(KeyTimeProperty)!;
        set => SetValue(KeyTimeProperty, value);
    }

    /// <summary>StaticResource key for the KeyTime (template visual states).</summary>
    public string? DeferredKeyTimeKey { get; set; }

    /// <summary>Value applied once the key time passes.</summary>
    public bool Value
    {
        get => (bool)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    object? IKeyFrame.Value
    {
        get => Value;
        set => Value = value switch
        {
            bool b => b,
            null => false,
            _ => throw new ArgumentException($"Cannot convert '{value.GetType().Name}' to bool.", nameof(value)),
        };
    }
}

/// <summary>Jump-cut boolean frame (matches DiscreteBooleanKeyFrame).</summary>
public sealed class DiscreteBooleanKeyFrame : BooleanKeyFrame
{
    /// <summary>Empty frame.</summary>
    public DiscreteBooleanKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public DiscreteBooleanKeyFrame(bool value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscreteBooleanKeyFrame(bool value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Boolean frame list (matches BooleanKeyFrameCollection).</summary>
public sealed class BooleanKeyFrameCollection : System.Collections.ObjectModel.Collection<BooleanKeyFrame>
{
}

/// <summary>
/// Key-framed boolean animation (matches BooleanAnimationUsingKeyFrames):
/// jump-cuts through resolved key times. Empty holds the origin; before
/// the first key holds the origin; past the last holds its value
/// (order-independent: the greatest key time at or before the clock wins,
/// like ObjectAnimationUsingKeyFrames). WPF has no interpolated boolean
/// frames, so Value/KeyTime are the whole contract.
/// </summary>
public class BooleanAnimationUsingKeyFrames : AnimationTimeline, IKeyFrameAnimation
{
    /// <summary>Frames in collection order.</summary>
    public BooleanKeyFrameCollection KeyFrames { get; } = new();

    /// <inheritdoc />
    System.Collections.IList IKeyFrameAnimation.KeyFrames
    {
        get => KeyFrames;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var frames = new List<BooleanKeyFrame>(value.Count);
            foreach (var item in value)
                frames.Add(item as BooleanKeyFrame ??
                    throw new ArgumentException($"KeyFrames accepts BooleanKeyFrame items, not '{item?.GetType().Name ?? "<null>"}'.", nameof(value)));
            KeyFrames.Clear();
            foreach (var frame in frames) KeyFrames.Add(frame);
        }
    }

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(bool);

    /// <summary>
    /// Automatic-duration resolution: the latest absolute key time, or a
    /// 1-second fallback when every key is Uniform/Percent (shared with
    /// ObjectAnimationUsingKeyFrames.GetNaturalDuration).
    /// </summary>
    internal TimeSpan GetNaturalDuration()
    {
        if (KeyFrames.Count == 0) return TimeSpan.Zero;
        var latest = TimeSpan.Zero;
        var anyAbsolute = false;
        foreach (var frame in KeyFrames)
        {
            if (frame.KeyTime.Type != KeyTimeType.TimeSpan) continue;
            anyAbsolute = true;
            if (frame.KeyTime.Time > latest) latest = frame.KeyTime.Time;
        }
        return anyAbsolute ? latest : TimeSpan.FromSeconds(1);
    }

    /// <summary>Typed sample at the clock position.</summary>
    public bool GetCurrentValue(bool defaultOriginValue, bool defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        if (KeyFrames.Count == 0) return defaultOriginValue;
        var duration = animationClock.IterationDuration;
        var current = defaultOriginValue;
        var best = TimeSpan.MinValue;
        var any = false;
        for (var i = 0; i < KeyFrames.Count; i++)
        {
            var at = KeyFrames[i].KeyTime.Resolve(duration, i, KeyFrames.Count);
            if (animationClock.CurrentTime >= at && (!any || at >= best))
            {
                best = at;
                current = KeyFrames[i].Value;
                any = true;
            }
        }
        return current;
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToBool(defaultOriginValue), ToBool(defaultDestinationValue), animationClock);

    private static bool ToBool(object? value) => value is bool b ? b : false;
}

/// <summary>Timeline container base (matches TimelineGroup : Timeline).</summary>
public class TimelineGroup : Timeline
{
    private readonly TimelineCollection _children = new();

    /// <summary>Child timelines (Storyboard.Children shares this shape).</summary>
    public TimelineCollection Children => _children;
}

/// <summary>Duration-slip rule (matches SlipBehavior; Grow=0 live-verified).</summary>
public enum SlipBehavior
{
    /// <summary>Group duration stretches to the longest child.</summary>
    Grow = 0,

    /// <summary>Children slip to fit the group duration.</summary>
    Slip = 1,
}

/// <summary>Parallel timeline (matches ParallelTimeline : TimelineGroup).</summary>
public class ParallelTimeline : TimelineGroup
{
    /// <summary>Empty group.</summary>
    public ParallelTimeline()
    {
    }

    /// <summary>Group with a begin offset (live-verified shape).</summary>
    public ParallelTimeline(TimeSpan? beginTime) => BeginTime = beginTime;

    /// <summary>Group with a begin offset and duration (live-verified shape).</summary>
    public ParallelTimeline(TimeSpan? beginTime, Duration duration)
    {
        BeginTime = beginTime;
        Duration = duration;
    }

    /// <summary>Group with a begin offset, duration and repetition (live-verified shape).</summary>
    public ParallelTimeline(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
    {
        BeginTime = beginTime;
        Duration = duration;
        RepeatBehavior = repeatBehavior;
    }

    /// <summary>How children fit the group duration (default Grow).</summary>
    public SlipBehavior SlipBehavior { get; set; } = SlipBehavior.Grow;
}

/// <summary>Sine easing (matches SineEase; EaseInCore = 1-cos(t*pi/2), live-verified).</summary>
public sealed class SineEase : EasingFunctionBase
{
    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        1.0 - Math.Cos(normalizedTime * Math.PI / 2.0);
}

/// <summary>Cubic easing (matches CubicEase; EaseInCore = t^3, live-verified).</summary>
public sealed class CubicEase : EasingFunctionBase
{
    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        normalizedTime * normalizedTime * normalizedTime;
}

/// <summary>Quadratic easing (matches QuadraticEase; EaseInCore = t^2, live-verified).</summary>
public sealed class QuadraticEase : EasingFunctionBase
{
    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        normalizedTime * normalizedTime;
}

// Quartic easing (matches QuarticEase; EaseInCore = t^4, live-verified
// against real WPF on net10.0-windows: EaseIn f(0.5) = 0.0625, EaseOut
// default with f(0.5) = 0.9375, EaseInOut f(0.25) = 0.03125).
public sealed class QuarticEase : EasingFunctionBase
{
    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        normalizedTime * normalizedTime * normalizedTime * normalizedTime;
}

/// <summary>Circle easing (matches CircleEase; EaseInCore = 1-sqrt(1-t^2), live-verified).</summary>
public sealed class CircleEase : EasingFunctionBase
{
    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - normalizedTime * normalizedTime));
}

/// <summary>Back easing (matches BackEase; EaseInCore = t^3-Amplitude*t*sin(pi*t), live-verified).</summary>
public sealed class BackEase : EasingFunctionBase
{
    /// <summary>Overshoot strength (matches BackEase.Amplitude; default 1 like WPF).</summary>
    public double Amplitude { get; set; } = 1.0;

    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) =>
        normalizedTime * normalizedTime * normalizedTime -
        Amplitude * normalizedTime * Math.Sin(normalizedTime * Math.PI);
}
