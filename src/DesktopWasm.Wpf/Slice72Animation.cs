namespace DesktopWasm.Wpf;

// Slice-72 animation remainder (live shapes verified 2026-10-26 with
// Mono.Cecil against the 9.0.20 PresentationCore/PresentationFramework ref
// packs): the Int32 and Thickness key-frame families plus the linear
// PointAnimation and the ClockState enum. Converted
// Microsoft.Xaml.Behaviors callers pin the member surface:
// ExtendedVisualStateManager.GetValueFromTimeline reads KeyFrames/Item/Value
// off both UsingKeyFrames animations and To off Int32Animation, and
// ChangePropertyAction.CreatePointAnimation drives From/To on PointAnimation.
//
// Retained limits, shared with the Double family in AnimationKeyframes.cs:
// IsCumulative is retained (no repeat-index plumbing in the deterministic
// pump); Storyboard children still run DoubleAnimation only.

/// <summary>Clock lifetime (matches System.Windows.Media.Animation.ClockState).</summary>
public enum ClockState
{
    /// <summary>Clock is running.</summary>
    Active = 0,

    /// <summary>Clock holds its final value.</summary>
    Filling = 1,

    /// <summary>Clock is stopped.</summary>
    Stopped = 2,
}

/// <summary>
/// Int32 animation base (matches System.Windows.Media.Animation.Int32AnimationBase):
/// the typed sampling contract both Int32 animations share.
/// </summary>
public abstract class Int32AnimationBase : AnimationTimeline
{
    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(int);

    /// <summary>Samples the animation (default holds the destination).</summary>
    public virtual int GetCurrentValue(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return defaultDestinationValue;
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToInt32(defaultOriginValue), ToInt32(defaultDestinationValue), animationClock);

    private static int ToInt32(object? value) => value switch
    {
        int i => i,
        double d => (int)d,
        float f => (int)f,
        long l => (int)l,
        _ => 0,
    };
}

/// <summary>Int32 key-frame base (matches System.Windows.Media.Animation.Int32KeyFrame).</summary>
public abstract class Int32KeyFrame : Freezable, IKeyFrame
{
    /// <summary>Identifies the KeyTime store.</summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register("KeyTime", typeof(KeyTime), typeof(Int32KeyFrame), KeyTime.Uniform);

    /// <summary>Identifies the Value store.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(int), typeof(Int32KeyFrame), 0);

    /// <summary>Empty frame.</summary>
    protected Int32KeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value (live-verified shape).</summary>
    protected Int32KeyFrame(int value) => Value = value;

    /// <summary>Timed frame carrying a value (live-verified shape).</summary>
    protected Int32KeyFrame(int value, KeyTime keyTime)
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

    /// <summary>Value applied at the key time.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    object? IKeyFrame.Value
    {
        get => Value;
        set => Value = value switch
        {
            int i => i,
            double d => (int)d,
            float f => (int)f,
            long l => (int)l,
            null => 0,
            _ => throw new ArgumentException($"Cannot convert '{value.GetType().Name}' to int.", nameof(value)),
        };
    }

    /// <summary>
    /// Interpolates from a base value toward this frame (matches
    /// Int32KeyFrame.InterpolateValue; default is linear with truncation).
    /// </summary>
    public virtual int InterpolateValue(int baseValue, double keyFrameProgress) =>
        baseValue + (int)((Value - baseValue) * keyFrameProgress);
}

/// <summary>Jump-cut int frame (matches DiscreteInt32KeyFrame).</summary>
public sealed class DiscreteInt32KeyFrame : Int32KeyFrame
{
    /// <summary>Empty frame.</summary>
    public DiscreteInt32KeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public DiscreteInt32KeyFrame(int value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscreteInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <inheritdoc />
    public override int InterpolateValue(int baseValue, double keyFrameProgress) =>
        keyFrameProgress >= 1.0 ? Value : baseValue;
}

/// <summary>Linear int frame (matches LinearInt32KeyFrame).</summary>
public sealed class LinearInt32KeyFrame : Int32KeyFrame
{
    /// <summary>Empty frame.</summary>
    public LinearInt32KeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public LinearInt32KeyFrame(int value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public LinearInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Eased int frame (matches EasingInt32KeyFrame).</summary>
public sealed class EasingInt32KeyFrame : Int32KeyFrame
{
    /// <summary>Empty frame.</summary>
    public EasingInt32KeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public EasingInt32KeyFrame(int value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public EasingInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed frame with an easing curve (live-verified shape).</summary>
    public EasingInt32KeyFrame(int value, KeyTime keyTime, IEasingFunction? easingFunction)
        : base(value, keyTime) => EasingFunction = easingFunction;

    /// <summary>Easing curve for the segment ending at this frame (null = linear).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <inheritdoc />
    public override int InterpolateValue(int baseValue, double keyFrameProgress) =>
        baseValue + (int)((Value - baseValue) * (EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress));
}

/// <summary>Int32 frame list (matches System.Windows.Media.Animation.Int32KeyFrameCollection).</summary>
public sealed class Int32KeyFrameCollection : Freezable, IList<Int32KeyFrame>, System.Collections.IList
{
    private readonly List<Int32KeyFrame> _items = new();

    /// <summary>Empty collection.</summary>
    public Int32KeyFrameCollection()
    {
    }

    /// <summary>Shared empty list (matches Int32KeyFrameCollection.Empty).</summary>
    public static Int32KeyFrameCollection Empty { get; } = new();

    /// <summary>Frame count.</summary>
    public int Count => _items.Count;

    /// <summary>Never fixed-size.</summary>
    public bool IsFixedSize => false;

    /// <summary>Frozen collections reject writes.</summary>
    public bool IsReadOnly => IsFrozen;

    /// <summary>Never thread-safe (single-threaded WASM).</summary>
    public bool IsSynchronized => false;

    /// <summary>List identity lock.</summary>
    public object SyncRoot => _items;

    /// <summary>Frame access (live-verified get/set shape).</summary>
    public Int32KeyFrame this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfFrozen();
            _items[index] = value;
        }
    }

    /// <summary>Appends a frame, returning its index (matches Add).</summary>
    public int Add(Int32KeyFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        ThrowIfFrozen();
        _items.Add(keyFrame);
        return _items.Count - 1;
    }

    void ICollection<Int32KeyFrame>.Add(Int32KeyFrame item) => Add(item);

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfFrozen();
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(Int32KeyFrame keyFrame) => keyFrame is not null && _items.Contains(keyFrame);

    /// <inheritdoc />
    public void CopyTo(Int32KeyFrame[] array, int index) => _items.CopyTo(array, index);

    /// <inheritdoc />
    public IEnumerator<Int32KeyFrame> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(Int32KeyFrame keyFrame) => keyFrame is null ? -1 : _items.IndexOf(keyFrame);

    /// <inheritdoc />
    public void Insert(int index, Int32KeyFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        ThrowIfFrozen();
        _items.Insert(index, keyFrame);
    }

    /// <inheritdoc />
    public bool Remove(Int32KeyFrame keyFrame)
    {
        ThrowIfFrozen();
        return keyFrame is not null && _items.Remove(keyFrame);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _items.RemoveAt(index);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    int System.Collections.IList.Add(object? value) =>
        value is Int32KeyFrame frame ? Add(frame)
        : throw new ArgumentException($"'{value?.GetType().Name}' is not an '{nameof(Int32KeyFrame)}'.", nameof(value));

    bool System.Collections.IList.Contains(object? value) => value is Int32KeyFrame frame && Contains(frame);

    int System.Collections.IList.IndexOf(object? value) => value is Int32KeyFrame frame ? IndexOf(frame) : -1;

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is Int32KeyFrame frame) Insert(index, frame);
        else throw new ArgumentException($"'{value?.GetType().Name}' is not an '{nameof(Int32KeyFrame)}'.", nameof(value));
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is Int32KeyFrame frame) Remove(frame);
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is Int32KeyFrame frame) this[index] = frame;
            else throw new ArgumentException($"'{value?.GetType().Name}' is not an '{nameof(Int32KeyFrame)}'.", nameof(value));
        }
    }

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>Modifiable deep copy (each frame clones so the copy edits alone).</summary>
    public new Int32KeyFrameCollection Clone()
    {
        var clone = new Int32KeyFrameCollection();
        foreach (var frame in _items)
            clone._items.Add((Int32KeyFrame)frame.Clone());
        return clone;
    }

    /// <summary>Identical to Clone here (no per-frame state to snapshot).</summary>
    public new Int32KeyFrameCollection CloneCurrentValue() => Clone();

}

/// <summary>
/// Key-framed int animation (matches System.Windows.Media.Animation.Int32AnimationUsingKeyFrames):
/// interpolates through resolved key times with the same contract as the
/// Double family (empty holds the origin; before the first holds the
/// origin; past the last holds its value; ending-frame dispatch through
/// <see cref="Int32KeyFrame.InterpolateValue"/>).
/// </summary>
public class Int32AnimationUsingKeyFrames : Int32AnimationBase
{
    private Int32KeyFrameCollection _keyFrames = new();

    /// <summary>Frames in collection order (live-verified get/set shape).</summary>
    public Int32KeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => _keyFrames = value ?? new Int32KeyFrameCollection();
    }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <summary>Whether the frames were ever assigned (matches ShouldSerializeKeyFrames).</summary>
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;

    /// <summary>Typed sample at the clock position.</summary>
    public override int GetCurrentValue(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var value = Sample(defaultOriginValue, defaultDestinationValue, animationClock);
        return IsAdditive ? defaultOriginValue + value : value;
    }

    private int Sample(int origin, int destination, AnimationClock clock)
    {
        if (KeyFrames.Count == 0) return origin;
        var duration = clock.IterationDuration;
        var ordered = new List<(TimeSpan At, Int32KeyFrame Frame)>(KeyFrames.Count);
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

    private static int Interpolate((TimeSpan At, Int32KeyFrame Frame) from, (TimeSpan At, Int32KeyFrame Frame) to, TimeSpan at)
    {
        if (to.At <= from.At) return to.Frame.Value;
        var progress = (at - from.At).Ticks / (double)(to.At - from.At).Ticks;
        progress = double.IsNaN(progress) ? 0.0 : Math.Clamp(progress, 0.0, 1.0);
        return to.Frame.InterpolateValue(from.Frame.Value, progress);
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new Int32AnimationUsingKeyFrames Clone() => (Int32AnimationUsingKeyFrames)MemberwiseClone();
}

/// <summary>Linear int animation (matches System.Windows.Media.Animation.Int32Animation).</summary>
public class Int32Animation : Int32AnimationBase
{
    /// <summary>Start value (defaults to the current value at Begin).</summary>
    public int? From { get; set; }

    /// <summary>End value.</summary>
    public int? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public int? By { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <summary>Empty animation.</summary>
    public Int32Animation()
    {
    }

    /// <summary>To value over a duration (live-verified shape).</summary>
    public Int32Animation(int toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>To value over a duration with a fill rule (live-verified shape).</summary>
    public Int32Animation(int toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>From/to over a duration (live-verified shape).</summary>
    public Int32Animation(int fromValue, int toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>From/to over a duration with a fill rule (live-verified shape).</summary>
    public Int32Animation(int fromValue, int toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>Typed sample at the clock position.</summary>
    public override int GetCurrentValue(int defaultOriginValue, int defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var eased = EasingFunction?.Ease(ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0))
            ?? ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0);
        var start = From ?? defaultOriginValue;
        var end = To ?? (By.HasValue ? start + By.Value : defaultDestinationValue);
        var value = start + (int)((end - start) * eased);
        return IsAdditive ? defaultOriginValue + value : value;
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new Int32Animation Clone() => (Int32Animation)MemberwiseClone();
}

/// <summary>
/// Thickness animation base (matches System.Windows.Media.Animation.ThicknessAnimationBase):
/// the typed sampling contract both Thickness animations share.
/// </summary>
public abstract class ThicknessAnimationBase : AnimationTimeline
{
    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Thickness);

    /// <summary>Samples the animation (default holds the destination).</summary>
    public virtual Thickness GetCurrentValue(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return defaultDestinationValue;
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToThickness(defaultOriginValue), ToThickness(defaultDestinationValue), animationClock);

    private static Thickness ToThickness(object? value) => value is Thickness thickness ? thickness : new Thickness(0, 0, 0, 0);
}

/// <summary>Thickness key-frame base (matches System.Windows.Media.Animation.ThicknessKeyFrame).</summary>
public abstract class ThicknessKeyFrame : Freezable, IKeyFrame, IKeyTimeDeferred
{
    /// <summary>Identifies the KeyTime store.</summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register("KeyTime", typeof(KeyTime), typeof(ThicknessKeyFrame), KeyTime.Uniform);

    /// <summary>Identifies the Value store.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(Thickness), typeof(ThicknessKeyFrame), new Thickness(0, 0, 0, 0));

    /// <summary>Empty frame.</summary>
    protected ThicknessKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value (live-verified shape).</summary>
    protected ThicknessKeyFrame(Thickness value) => Value = value;

    /// <summary>Timed frame carrying a value (live-verified shape).</summary>
    protected ThicknessKeyFrame(Thickness value, KeyTime keyTime)
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

    /// <summary>Value applied at the key time.</summary>
    public Thickness Value
    {
        get => (Thickness)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    object? IKeyFrame.Value
    {
        get => Value;
        set => Value = value switch
        {
            Thickness thickness => thickness,
            null => new Thickness(0, 0, 0, 0),
            _ => throw new ArgumentException($"Cannot convert '{value.GetType().Name}' to Thickness.", nameof(value)),
        };
    }

    /// <summary>
    /// Interpolates from a base value toward this frame (matches
    /// ThicknessKeyFrame.InterpolateValue; default is linear).
    /// </summary>
    public virtual Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress) => Lerp(baseValue, Value, keyFrameProgress);

    private static Thickness Lerp(Thickness from, Thickness to, double progress) => new(
        from.Left + ((to.Left - from.Left) * progress),
        from.Top + ((to.Top - from.Top) * progress),
        from.Right + ((to.Right - from.Right) * progress),
        from.Bottom + ((to.Bottom - from.Bottom) * progress));
}

/// <summary>Jump-cut thickness frame (matches DiscreteThicknessKeyFrame).</summary>
public sealed class DiscreteThicknessKeyFrame : ThicknessKeyFrame
{
    /// <summary>Empty frame.</summary>
    public DiscreteThicknessKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public DiscreteThicknessKeyFrame(Thickness value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscreteThicknessKeyFrame(Thickness value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <inheritdoc />
    public override Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress) =>
        keyFrameProgress >= 1.0 ? Value : baseValue;
}

/// <summary>Linear thickness frame (matches LinearThicknessKeyFrame).</summary>
public sealed class LinearThicknessKeyFrame : ThicknessKeyFrame
{
    /// <summary>Empty frame.</summary>
    public LinearThicknessKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public LinearThicknessKeyFrame(Thickness value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public LinearThicknessKeyFrame(Thickness value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Eased thickness frame (matches EasingThicknessKeyFrame).</summary>
public sealed class EasingThicknessKeyFrame : ThicknessKeyFrame
{
    /// <summary>Empty frame.</summary>
    public EasingThicknessKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public EasingThicknessKeyFrame(Thickness value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public EasingThicknessKeyFrame(Thickness value, KeyTime keyTime) : base(value, keyTime)
    {
    }

    /// <summary>Timed frame with an easing curve (live-verified shape).</summary>
    public EasingThicknessKeyFrame(Thickness value, KeyTime keyTime, IEasingFunction? easingFunction)
        : base(value, keyTime) => EasingFunction = easingFunction;

    /// <summary>Easing curve for the segment ending at this frame (null = linear).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <inheritdoc />
    public override Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress)
    {
        var eased = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var to = Value;
        return new Thickness(
            baseValue.Left + ((to.Left - baseValue.Left) * eased),
            baseValue.Top + ((to.Top - baseValue.Top) * eased),
            baseValue.Right + ((to.Right - baseValue.Right) * eased),
            baseValue.Bottom + ((to.Bottom - baseValue.Bottom) * eased));
    }
}

/// <summary>Thickness frame list (matches System.Windows.Media.Animation.ThicknessKeyFrameCollection).</summary>
public sealed class ThicknessKeyFrameCollection : Freezable, IList<ThicknessKeyFrame>, System.Collections.IList
{
    private readonly List<ThicknessKeyFrame> _items = new();

    /// <summary>Empty collection.</summary>
    public ThicknessKeyFrameCollection()
    {
    }

    /// <summary>Shared empty list (matches ThicknessKeyFrameCollection.Empty).</summary>
    public static ThicknessKeyFrameCollection Empty { get; } = new();

    /// <summary>Frame count.</summary>
    public int Count => _items.Count;

    /// <summary>Never fixed-size.</summary>
    public bool IsFixedSize => false;

    /// <summary>Frozen collections reject writes.</summary>
    public bool IsReadOnly => IsFrozen;

    /// <summary>Never thread-safe (single-threaded WASM).</summary>
    public bool IsSynchronized => false;

    /// <summary>List identity lock.</summary>
    public object SyncRoot => _items;

    /// <summary>Frame access (live-verified get/set shape).</summary>
    public ThicknessKeyFrame this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfFrozen();
            _items[index] = value;
        }
    }

    /// <summary>Appends a frame, returning its index (matches Add).</summary>
    public int Add(ThicknessKeyFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        ThrowIfFrozen();
        _items.Add(keyFrame);
        return _items.Count - 1;
    }

    void ICollection<ThicknessKeyFrame>.Add(ThicknessKeyFrame item) => Add(item);

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfFrozen();
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(ThicknessKeyFrame keyFrame) => keyFrame is not null && _items.Contains(keyFrame);

    /// <inheritdoc />
    public void CopyTo(ThicknessKeyFrame[] array, int index) => _items.CopyTo(array, index);

    /// <inheritdoc />
    public IEnumerator<ThicknessKeyFrame> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(ThicknessKeyFrame keyFrame) => keyFrame is null ? -1 : _items.IndexOf(keyFrame);

    /// <inheritdoc />
    public void Insert(int index, ThicknessKeyFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        ThrowIfFrozen();
        _items.Insert(index, keyFrame);
    }

    /// <inheritdoc />
    public bool Remove(ThicknessKeyFrame keyFrame)
    {
        ThrowIfFrozen();
        return keyFrame is not null && _items.Remove(keyFrame);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _items.RemoveAt(index);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    int System.Collections.IList.Add(object? value) =>
        value is ThicknessKeyFrame frame ? Add(frame)
        : throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(ThicknessKeyFrame)}'.", nameof(value));

    bool System.Collections.IList.Contains(object? value) => value is ThicknessKeyFrame frame && Contains(frame);

    int System.Collections.IList.IndexOf(object? value) => value is ThicknessKeyFrame frame ? IndexOf(frame) : -1;

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is ThicknessKeyFrame frame) Insert(index, frame);
        else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(ThicknessKeyFrame)}'.", nameof(value));
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is ThicknessKeyFrame frame) Remove(frame);
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is ThicknessKeyFrame frame) this[index] = frame;
            else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(ThicknessKeyFrame)}'.", nameof(value));
        }
    }

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>Modifiable deep copy (each frame clones so the copy edits alone).</summary>
    public new ThicknessKeyFrameCollection Clone()
    {
        var clone = new ThicknessKeyFrameCollection();
        foreach (var frame in _items)
            clone._items.Add((ThicknessKeyFrame)frame.Clone());
        return clone;
    }

    /// <summary>Identical to Clone here (no per-frame state to snapshot).</summary>
    public new ThicknessKeyFrameCollection CloneCurrentValue() => Clone();

}

/// <summary>
/// Key-framed thickness animation (matches System.Windows.Media.Animation.ThicknessAnimationUsingKeyFrames):
/// interpolates through resolved key times with the same contract as the
/// Double family, dispatching each segment through
/// <see cref="ThicknessKeyFrame.InterpolateValue"/>.
/// </summary>
public class ThicknessAnimationUsingKeyFrames : ThicknessAnimationBase
{
    private ThicknessKeyFrameCollection _keyFrames = new();

    /// <summary>Frames in collection order (live-verified get/set shape).</summary>
    public ThicknessKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => _keyFrames = value ?? new ThicknessKeyFrameCollection();
    }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <summary>Whether the frames were ever assigned (matches ShouldSerializeKeyFrames).</summary>
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;

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
    public override Thickness GetCurrentValue(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var value = Sample(defaultOriginValue, defaultDestinationValue, animationClock);
        if (!IsAdditive) return value;
        return new Thickness(
            defaultOriginValue.Left + value.Left,
            defaultOriginValue.Top + value.Top,
            defaultOriginValue.Right + value.Right,
            defaultOriginValue.Bottom + value.Bottom);
    }

    private Thickness Sample(Thickness origin, Thickness destination, AnimationClock clock)
    {
        if (KeyFrames.Count == 0) return origin;
        var duration = clock.IterationDuration;
        var ordered = new List<(TimeSpan At, ThicknessKeyFrame Frame)>(KeyFrames.Count);
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

    private static Thickness Interpolate((TimeSpan At, ThicknessKeyFrame Frame) from, (TimeSpan At, ThicknessKeyFrame Frame) to, TimeSpan at)
    {
        if (to.At <= from.At) return to.Frame.Value;
        var progress = (at - from.At).Ticks / (double)(to.At - from.At).Ticks;
        progress = double.IsNaN(progress) ? 0.0 : Math.Clamp(progress, 0.0, 1.0);
        return to.Frame.InterpolateValue(from.Frame.Value, progress);
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new ThicknessAnimationUsingKeyFrames Clone() => (ThicknessAnimationUsingKeyFrames)MemberwiseClone();
}

/// <summary>
/// Point animation base (matches System.Windows.Media.Animation.PointAnimationBase):
/// the typed sampling contract both point animations share.
/// </summary>
public abstract class PointAnimationBase : AnimationTimeline
{
    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Point);

    /// <summary>Samples the animation (default holds the destination).</summary>
    public virtual Point GetCurrentValue(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return defaultDestinationValue;
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToPoint(defaultOriginValue), ToPoint(defaultDestinationValue), animationClock);

    private static Point ToPoint(object? value) => value is Point point ? point : new Point(0, 0);
}

/// <summary>Linear point animation (matches System.Windows.Media.Animation.PointAnimation).</summary>
public class PointAnimation : PointAnimationBase
{
    /// <summary>Start point (defaults to the current value at Begin).</summary>
    public Point? From { get; set; }

    /// <summary>End point.</summary>
    public Point? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public Point? By { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>When true, the interpolated value offsets from the origin.</summary>
    public bool IsAdditive { get; set; }

    /// <summary>Retained: repeat accumulation needs repeat-index plumbing.</summary>
    public bool IsCumulative { get; set; }

    /// <summary>Empty animation.</summary>
    public PointAnimation()
    {
    }

    /// <summary>To value over a duration (live-verified shape).</summary>
    public PointAnimation(Point toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>To value over a duration with a fill rule (live-verified shape).</summary>
    public PointAnimation(Point toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>From/to over a duration (live-verified shape).</summary>
    public PointAnimation(Point fromValue, Point toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>From/to over a duration with a fill rule (live-verified shape).</summary>
    public PointAnimation(Point fromValue, Point toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>Typed sample at the clock position.</summary>
    public override Point GetCurrentValue(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var eased = EasingFunction?.Ease(ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0))
            ?? ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0);
        var start = From ?? defaultOriginValue;
        var end = To ?? (By.HasValue
            ? new Point(start.X + By.Value.X, start.Y + By.Value.Y)
            : defaultDestinationValue);
        var value = new Point(
            start.X + ((end.X - start.X) * eased),
            start.Y + ((end.Y - start.Y) * eased));
        return IsAdditive ? new Point(defaultOriginValue.X + value.X, defaultOriginValue.Y + value.Y) : value;
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new PointAnimation Clone() => (PointAnimation)MemberwiseClone();
}