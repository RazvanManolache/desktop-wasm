using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf;

// Animation clocks (PresentationCore/PresentationFramework subset, real
// timing, no render thread): PowerEase, AnimationTimeline,
// ObjectAnimationUsingKeyFrames (+ KeyTime/ObjectKeyFrame/
// DiscreteObjectKeyFrame/ObjectKeyFrameCollection), ThicknessAnimation,
// ColorAnimation(ColorAnimationBase), AnimationClock and HandoffBehavior,
// pumped deterministically by WpfAnimationClock (see Animation.cs).
// UIElement.BeginAnimation registers here; Storyboard keeps its
// DoubleAnimation-only path in Animation.cs.

/// <summary>Easing direction (matches System.Windows.Media.Animation.EasingMode).</summary>
public enum EasingMode
{
    EaseIn,
    EaseOut,
    EaseInOut,
}

/// <summary>
 /// Easing base (matches EasingFunctionBase : Freezable, IEasingFunction):
 /// Ease applies the mode around the subclass EaseInCore curve.
/// </summary>
public abstract class EasingFunctionBase : Freezable, IEasingFunction
{
    /// <summary>Easing direction (default EaseIn, like WPF).</summary>
    public EasingMode EasingMode { get; set; } = EasingMode.EaseIn;

    /// <summary>Mode-adjusted easing of a 0..1 progress value.</summary>
    public double Ease(double normalizedTime)
    {
        var t = double.IsNaN(normalizedTime) ? 0.0 : Math.Clamp(normalizedTime, 0.0, 1.0);
        return EasingMode switch
        {
            EasingMode.EaseIn => EaseInCore(t),
            EasingMode.EaseOut => 1.0 - EaseInCore(1.0 - t),
            _ => t < 0.5
                ? EaseInCore(t * 2.0) / 2.0
                : 1.0 - EaseInCore((1.0 - t) * 2.0) / 2.0,
        };
    }

    /// <summary>Ease-in curve (subclasses implement; PowerEase uses t^Power).</summary>
    protected abstract double EaseInCore(double normalizedTime);
}

/// <summary>Power curve easing (matches PowerEase; Power defaults to 2 like WPF).</summary>
public sealed class PowerEase : EasingFunctionBase
{
    /// <summary>Exponent applied to the normalized time.</summary>
    public double Power { get; set; } = 2.0;

    /// <inheritdoc />
    protected override double EaseInCore(double normalizedTime) => Math.Pow(normalizedTime, Power);
}

/// <summary>Animation replacement rule (matches HandoffBehavior).</summary>
public enum HandoffBehavior
{
    SnapshotAndReplace,
    Compose,
}

/// <summary>Key-time kind (matches KeyTimeType).</summary>
public enum KeyTimeType
{
    Uniform,
    Paced,
    Percent,
    TimeSpan,
}

/// <summary>Key-frame time (matches KeyTime): absolute, percent-of-duration, uniform or paced.</summary>
public readonly struct KeyTime : IEquatable<KeyTime>
{
    private readonly TimeSpan _time;
    private readonly double _percent;
    private readonly KeyTimeType _type;

    private KeyTime(TimeSpan time, double percent, KeyTimeType type)
    {
        _time = time;
        _percent = percent;
        _type = type;
    }

    /// <summary>Absolute key time.</summary>
    public static KeyTime FromTimeSpan(TimeSpan keyTime) => new(keyTime, 0, KeyTimeType.TimeSpan);

    /// <summary>Percent-of-iteration key time (0..100, like WPF).</summary>
    public static KeyTime FromPercent(double percent)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "KeyTime percent must be between 0 and 100.");
        return new KeyTime(TimeSpan.Zero, percent, KeyTimeType.Percent);
    }

    /// <summary>Evenly spaced key time.</summary>
    public static KeyTime Uniform => new(TimeSpan.Zero, 0, KeyTimeType.Uniform);

    /// <summary>Distance-paced key time (rejected by key-frame evaluation; see Resolve).</summary>
    public static KeyTime Paced => new(TimeSpan.Zero, 0, KeyTimeType.Paced);

    /// <summary>Key-time kind.</summary>
    public KeyTimeType Type => _type;

    /// <summary>Absolute time (TimeSpan kind only).</summary>
    public TimeSpan Time => _type == KeyTimeType.TimeSpan
        ? _time
        : throw new InvalidOperationException($"KeyTime has no TimeSpan (kind is {_type}).");

    /// <summary>Implicit absolute key time (matches the WPF TimeSpan conversion).</summary>
    public static implicit operator KeyTime(TimeSpan keyTime) => FromTimeSpan(keyTime);

    /// <summary>Parses Uniform/Paced/percent/TimeSpan/seconds forms (shared
    /// by storyboard frames and CLR resource entries, like KeyTimeConverter).</summary>
    internal static KeyTime ParseKeyTimeText(string text)
    {
        var trimmed = text.Trim();
        if (string.Equals(trimmed, "Uniform", StringComparison.OrdinalIgnoreCase)) return Uniform;
        if (string.Equals(trimmed, "Paced", StringComparison.OrdinalIgnoreCase)) return Paced;
        if (trimmed.EndsWith("%", StringComparison.Ordinal) &&
            double.TryParse(trimmed[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            return FromPercent(percent);
        if (TimeSpan.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out var time))
            return FromTimeSpan(time);
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return FromTimeSpan(TimeSpan.FromSeconds(seconds));
        throw new FormatException($"'{text}' is not a KeyTime (Uniform/Paced/percent/0:0:0.5).");
    }

    /// <summary>
    /// Resolves to an absolute iteration time. Percent scales the iteration
    /// duration; Uniform spaces evenly ((index+1)/count); Paced has no
    /// distance metric on object key frames and fails precisely.
    /// </summary>
    internal TimeSpan Resolve(TimeSpan iterationDuration, int index, int count) =>
        _type switch
        {
            KeyTimeType.TimeSpan => _time,
            KeyTimeType.Percent => TimeSpan.FromTicks((long)(iterationDuration.Ticks * (_percent / 100.0))),
            KeyTimeType.Uniform => count <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(iterationDuration.Ticks * (index + 1) / count),
            _ => throw new InvalidOperationException(
                "KeyTime Paced needs distance-based pacing, which object key-frame evaluation does not implement."),
        };

    public bool Equals(KeyTime other) =>
        _type == other._type && _time.Equals(other._time) && _percent.Equals(other._percent);

    public override bool Equals(object? obj) => obj is KeyTime other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_time, _percent, _type);

    public static bool operator ==(KeyTime left, KeyTime right) => left.Equals(right);

    public static bool operator !=(KeyTime left, KeyTime right) => !left.Equals(right);

    public override string ToString() => _type switch
    {
        KeyTimeType.TimeSpan => _time.ToString(),
        KeyTimeType.Percent => _percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
        KeyTimeType.Paced => "Paced",
        _ => "Uniform",
    };
}

/// <summary>Single object key frame (matches ObjectKeyFrame : Freezable).</summary>
public abstract class ObjectKeyFrame : Freezable, IKeyTimeDeferred
{
    /// <summary>Empty frame (Uniform time, null value).</summary>
    protected ObjectKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    protected ObjectKeyFrame(object? value) => Value = value;

    /// <summary>Timed frame carrying a value.</summary>
    protected ObjectKeyFrame(object? value, KeyTime keyTime)
    {
        Value = value;
        KeyTime = keyTime;
    }

    /// <summary>When the frame takes effect (default Uniform, like WPF).</summary>
    public KeyTime KeyTime { get; set; } = KeyTime.Uniform;

    /// <summary>StaticResource key for the KeyTime (template visual states).</summary>
    public string? DeferredKeyTimeKey { get; set; }

    /// <summary>Value applied once the key time passes.</summary>
    public object? Value { get; set; }
}

/// <summary>Discrete object key frame (matches DiscreteObjectKeyFrame).</summary>
public sealed class DiscreteObjectKeyFrame : ObjectKeyFrame
{
    /// <summary>Empty frame (Uniform time, null value).</summary>
    public DiscreteObjectKeyFrame()
    {
    }

    /// <summary>Uniform-time frame carrying a value.</summary>
    public DiscreteObjectKeyFrame(object? value) : base(value)
    {
    }

    /// <summary>Timed frame carrying a value.</summary>
    public DiscreteObjectKeyFrame(object? value, KeyTime keyTime) : base(value, keyTime)
    {
    }
}

/// <summary>Key-frame list (matches ObjectKeyFrameCollection).</summary>
public sealed class ObjectKeyFrameCollection : Collection<ObjectKeyFrame>
{
}

/// <summary>
/// Value-animation base (matches AnimationTimeline : Timeline): declares the
 /// animated CLR type and evaluates one clock sample. Freezable plumbing
 /// (CreateInstanceCore) comes from Freezable so converted overrides like
 /// ScreenToGif BrushAnimation bind.
 /// </summary>
public abstract class AnimationTimeline : Timeline
{
    /// <summary>Animated CLR type (Color, double, Thickness, object, ...).</summary>
    public abstract Type TargetPropertyType { get; }

    /// <summary>Sample at the clock position (default: hold the destination).</summary>
    public virtual object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return defaultDestinationValue;
    }
}

/// <summary>
 /// Single clock sample (matches AnimationClock): the engine builds one per
 /// animated value per pump from the deterministic clock, so converted
 /// GetCurrentValue overrides (e.g. BrushAnimation delegating to
 /// ColorAnimation) see real iteration time and progress.
 /// </summary>
public sealed class AnimationClock
{
    internal AnimationClock(TimeSpan currentTime, double? currentProgress, TimeSpan iterationDuration, Timeline? timeline)
    {
        CurrentTime = currentTime;
        CurrentProgress = currentProgress;
        IterationDuration = iterationDuration;
        Timeline = timeline;
    }

    /// <summary>Direction-adjusted time within the current iteration.</summary>
    public TimeSpan CurrentTime { get; }

    /// <summary>Direction-adjusted 0..1 progress (null past the active edge).</summary>
    public double? CurrentProgress { get; }

    /// <summary>Timeline this clock samples.</summary>
    public Timeline? Timeline { get; }

    /// <summary>Single-iteration length (key-frame percents/uniforms scale it).</summary>
    internal TimeSpan IterationDuration { get; }

    /// <summary>Test/diagnostic clock (the engine builds the live ones).</summary>
    internal static AnimationClock CreateForTests(TimeSpan currentTime, double? currentProgress, TimeSpan iterationDuration) =>
        new(currentTime, currentProgress, iterationDuration, null);
}

/// <summary>Color animation base (matches ColorAnimationBase : AnimationTimeline).</summary>
public abstract class ColorAnimationBase : AnimationTimeline
{
    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Color);
}

/// <summary>Linear color interpolation (matches ColorAnimation).</summary>
public class ColorAnimation : ColorAnimationBase
{
    /// <summary>Start color (defaults to the current value at Begin).</summary>
    public Color? From { get; set; }

    /// <summary>End color.</summary>
    public Color? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public Color? By { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>Empty animation.</summary>
    public ColorAnimation()
    {
    }

    /// <summary>From/to over a duration (matches the original HideableTabControl/BrushAnimation shape).</summary>
    public ColorAnimation(Color fromValue, Color toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>From/to over a duration with a fill rule.</summary>
    public ColorAnimation(Color fromValue, Color toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>Typed sample at the clock position.</summary>
    public Color GetCurrentValue(Color defaultOriginValue, Color defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var eased = EasingFunction?.Ease(ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0))
            ?? ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0);
        var start = From ?? defaultOriginValue;
        var end = To ?? (By.HasValue ? Add(start, By.Value) : defaultDestinationValue);
        return Lerp(start, end, eased);
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToColor(defaultOriginValue), ToColor(defaultDestinationValue), animationClock);

    private static Color Add(Color left, Color right) => Color.FromArgb(
        (byte)Math.Min(255, left.A + right.A),
        (byte)Math.Min(255, left.R + right.R),
        (byte)Math.Min(255, left.G + right.G),
        (byte)Math.Min(255, left.B + right.B));

    private static byte LerpByte(byte from, byte to, double progress) =>
        (byte)Math.Round(from + ((to - from) * progress));

    private static Color Lerp(Color from, Color to, double progress) => Color.FromArgb(
        LerpByte(from.A, to.A, progress),
        LerpByte(from.R, to.R, progress),
        LerpByte(from.G, to.G, progress),
        LerpByte(from.B, to.B, progress));

    private static Color ToColor(object? value) => value is Color color ? color : Color.FromRgb(0, 0, 0);
}

/// <summary>Linear thickness interpolation (matches ThicknessAnimation).</summary>
public class ThicknessAnimation : AnimationTimeline
{
    /// <summary>Start thickness (defaults to the current value at Begin).</summary>
    public Thickness? From { get; set; }

    /// <summary>End thickness.</summary>
    public Thickness? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public Thickness? By { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>Empty animation.</summary>
    public ThicknessAnimation()
    {
    }

    /// <summary>From/to over a duration (matches the original HideableTabControl margin shape).</summary>
    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>From/to over a duration with a fill rule.</summary>
    public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Thickness);

    /// <summary>Typed sample at the clock position.</summary>
    public Thickness GetCurrentValue(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var eased = EasingFunction?.Ease(ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0))
            ?? ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0);
        var start = From ?? defaultOriginValue;
        var end = To ?? (By.HasValue
            ? new Thickness(start.Left + By.Value.Left, start.Top + By.Value.Top, start.Right + By.Value.Right, start.Bottom + By.Value.Bottom)
            : defaultDestinationValue);
        return new Thickness(
            start.Left + ((end.Left - start.Left) * eased),
            start.Top + ((end.Top - start.Top) * eased),
            start.Right + ((end.Right - start.Right) * eased),
            start.Bottom + ((end.Bottom - start.Bottom) * eased));
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToThickness(defaultOriginValue), ToThickness(defaultDestinationValue), animationClock);

    private static Thickness ToThickness(object? value) => value is Thickness thickness ? thickness : new Thickness(0);
}

/// <summary>
 /// Discrete object animation (matches ObjectAnimationUsingKeyFrames):
 /// holds the origin value until a key time passes, then holds that frame's
 /// value (order-independent: the greatest key time at or before the clock
 /// wins, so unsorted collections evaluate like WPF).
 /// </summary>
public class ObjectAnimationUsingKeyFrames : AnimationTimeline, IKeyFrameAnimation
{
    /// <summary>Frames in evaluation order.</summary>
    public ObjectKeyFrameCollection KeyFrames { get; } = new();

    /// <inheritdoc />
    System.Collections.IList IKeyFrameAnimation.KeyFrames
    {
        get => KeyFrames;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var frames = new List<ObjectKeyFrame>(value.Count);
            foreach (var item in value)
                frames.Add(item as ObjectKeyFrame ??
                    throw new ArgumentException($"KeyFrames accepts ObjectKeyFrame items, not '{item?.GetType().Name ?? "<null>"}'.", nameof(value)));
            KeyFrames.Clear();
            foreach (var frame in frames) KeyFrames.Add(frame);
        }
    }

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(object);

    /// <summary>
    /// Automatic-duration resolution: the latest absolute key time, or a
    /// 1-second fallback when every key is Uniform/Percent (those scale the
    /// iteration, so an absolute-only rule would divide by zero).
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

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        if (KeyFrames.Count == 0) return defaultOriginValue;
        var duration = animationClock.IterationDuration;
        object? current = defaultOriginValue;
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
}
