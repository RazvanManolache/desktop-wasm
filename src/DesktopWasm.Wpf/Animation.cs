using System.Runtime.CompilerServices;

namespace DesktopWasm.Wpf;

// Animation (PresentationCore subset, shim, not port): a deterministic,
// manually-pumped clock drives DoubleAnimation interpolation on converted
// and engine-built timelines. The browser host advances the clock once per
// frame (WasmWpfHost.Tick); tests advance it with virtual deltas. No
// render-thread, no composition target; easing runs through IEasingFunction
// (EasingFunctionBase/PowerEase in AnimationClocks.cs).

/// <summary>Timeline length: a span, Automatic (single step) or Forever.</summary>
public readonly struct Duration : IEquatable<Duration>
{
    public static readonly Duration Automatic = new();
    public static readonly Duration Forever = new(true);

    private readonly bool _forever;
    private readonly TimeSpan _span;
    private readonly bool _hasSpan;

    public Duration(TimeSpan span)
    {
        _span = span;
        _hasSpan = true;
    }

    private Duration(bool forever) => _forever = forever;

    public bool HasTimeSpan => _hasSpan;
    public TimeSpan TimeSpan => _hasSpan ? _span : throw new InvalidOperationException("Duration has no TimeSpan (Automatic/Forever).");

    public static implicit operator Duration(TimeSpan span) => new(span);

    public static Duration Parse(string value)
    {
        var text = value.Trim();
        if (text == "Automatic") return Automatic;
        if (text == "Forever") return Forever;
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var span))
            return new Duration(span);
        // Bare numbers read as seconds (VSM zero-length pulses write Duration="0").
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return new Duration(TimeSpan.FromSeconds(seconds));
        return new Duration(TimeSpan.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool Equals(Duration other) =>
        _forever == other._forever && _hasSpan == other._hasSpan && (!_hasSpan || _span.Equals(other._span));

    public override bool Equals(object? obj) => obj is Duration other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_forever, _hasSpan, _span);
    public static bool operator ==(Duration left, Duration right) => left.Equals(right);
    public static bool operator !=(Duration left, Duration right) => !left.Equals(right);
    public override string ToString() => _forever ? "Forever" : _hasSpan ? _span.ToString() : "Automatic";
}

/// <summary>Storyboard repetition: a count, Forever, or a total duration.</summary>
public readonly struct RepeatBehavior : IEquatable<RepeatBehavior>
{
    public static readonly RepeatBehavior Forever = new(double.PositiveInfinity);

    /// <summary>Iteration count (null for Forever/duration forms).</summary>
    public double? Count { get; }

    /// <summary>Total running time (null for count/Forever forms).</summary>
    public TimeSpan? RepeatDuration { get; }

    public RepeatBehavior(double count)
    {
        if (count < 0 || double.IsNaN(count)) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
    }

    public RepeatBehavior(TimeSpan duration) => RepeatDuration = duration;

    public static RepeatBehavior Parse(string value)
    {
        var text = value.Trim();
        if (text == "Forever") return Forever;
        if (text.EndsWith("x", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var count))
            return new RepeatBehavior(count);
        return new RepeatBehavior(TimeSpan.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool Equals(RepeatBehavior other) => Count.Equals(other.Count) && RepeatDuration.Equals(other.RepeatDuration);
    public override bool Equals(object? obj) => obj is RepeatBehavior other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Count, RepeatDuration);
    public static bool operator ==(RepeatBehavior left, RepeatBehavior right) => left.Equals(right);
    public static bool operator !=(RepeatBehavior left, RepeatBehavior right) => !left.Equals(right);
}

/// <summary>Easing hook (linear until implementations like PowerEase land).</summary>
public interface IEasingFunction
{
    double Ease(double normalizedTime);
}

/// <summary>Post-completion behavior: hold the final value or revert to base.</summary>
public enum FillBehavior
{
    HoldEnd,
    Stop,
}

/// <summary>Timeline base (PresentationCore subset): duration, begin offset, repetition.</summary>
public class Timeline : Animatable
{
    /// <summary>Raised after the final iteration completes (matches Timeline.Completed).</summary>
    public event EventHandler? Completed;

    /// <summary>Raises <see cref="Completed"/> (the pump calls it, not converted code).</summary>
    internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Applies the acceleration/deceleration curve (cubic, C1-continuous,
    /// monotone; normalized when the ratios sum past 1). Matches the
    /// qualitative WPF contract, not bit-exact easing.
    /// </summary>
    internal double ApplyTimingCurve(double progress)
    {
        var accel = AccelerationRatio;
        var decel = DecelerationRatio;
        if (accel == 0 && decel == 0) return progress;
        var sum = accel + decel;
        if (sum > 1)
        {
            accel /= sum;
            decel /= sum;
        }
        var norm = 1.0 - ((accel + decel) / 2.0);
        if (progress < accel)
            return accel == 0 ? 0.0 : (progress * progress / (2.0 * accel)) / norm;
        if (progress > 1.0 - decel)
        {
            if (decel == 0) return 1.0;
            var tail = 1.0 - progress;
            return 1.0 - ((tail * tail / (2.0 * decel)) / norm);
        }
        return ((accel / 2.0) + (progress - accel)) / norm;
    }

    /// <summary>Single-iteration length (Automatic resolves per subclass).</summary>
    public Duration Duration { get; set; } = Duration.Automatic;

    /// <summary>Resource key for the Duration (template resource references
    /// resolve at storyboard run, like deferred key times).</summary>
    internal string? DeferredDurationKey { get; set; }

    /// <summary>Delay before the first iteration.</summary>
    public TimeSpan? BeginTime { get; set; }

    /// <summary>Repetition (default one pass).</summary>
    public RepeatBehavior RepeatBehavior { get; set; } = new RepeatBehavior(1);

    /// <summary>Ping-pong each iteration.</summary>
    public bool AutoReverse { get; set; }

    /// <summary>What the last frame leaves behind (default HoldEnd).</summary>
    public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;

    private double _accelerationRatio;

    /// <summary>Speed-up fraction of the pass (0..1; normalized with deceleration).</summary>
    public double AccelerationRatio
    {
        get => _accelerationRatio;
        set => _accelerationRatio = CheckRatio(value, nameof(AccelerationRatio));
    }

    private double _decelerationRatio;

    /// <summary>Slow-down fraction of the pass (0..1; normalized with acceleration).</summary>
    public double DecelerationRatio
    {
        get => _decelerationRatio;
        set => _decelerationRatio = CheckRatio(value, nameof(DecelerationRatio));
    }

    private static double CheckRatio(double value, string name)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name, "Acceleration/DecelerationRatio must be between 0 and 1.");
        return value;
    }
}

/// <summary>Timeline list (matches System.Windows.Media.Animation.TimelineCollection).</summary>
public sealed class TimelineCollection : List<Timeline>
{
    /// <summary>Value enumerator (matches TimelineCollection.Enumerator; converted foreach binds here).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Timeline enumerator (matches System.Windows.Media.Animation.TimelineCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<Timeline>
    {
        private readonly List<Timeline> _list;
        private int _index;

        internal Enumerator(List<Timeline> list)
        {
            _list = list;
            _index = -1;
        }

        public Timeline Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Linear double interpolation between From/To (or From+By).</summary>
public class DoubleAnimation : AnimationTimeline
{
    /// <summary>Start value (defaults to the target's current value at Begin).</summary>
    public double? From { get; set; }

    /// <summary>End value.</summary>
    public double? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public double? By { get; set; }

    /// <summary>TemplateBinding path for From (template storyboard
    /// endpoints resolve against the templated parent at run, like
    /// deferred durations).</summary>
    internal string? DeferredFromBinding { get; set; }

    /// <summary>TemplateBinding path for To (see DeferredFromBinding).</summary>
    internal string? DeferredToBinding { get; set; }

    /// <summary>TemplateBinding path for By (see DeferredFromBinding).</summary>
    internal string? DeferredByBinding { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>Empty animation.</summary>
    public DoubleAnimation()
    {
    }

    /// <summary>To-value over a duration (matches System.Windows.Media.Animation.DoubleAnimation).</summary>
    public DoubleAnimation(double toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>To-value over a duration with a fill rule.</summary>
    public DoubleAnimation(double toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    /// <summary>From/to over a duration (matches the original HideableTabControl shape).</summary>
    public DoubleAnimation(double fromValue, double toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>From/to over a duration with a fill rule.</summary>
    public DoubleAnimation(double fromValue, double toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(double);

    internal double Evaluate(double from, double progress)
    {
        var start = From ?? from;
        var end = To ?? (By.HasValue ? start + By.Value : start);
        var eased = EasingFunction?.Ease(progress) ?? progress;
        return start + (end - start) * eased;
    }

    /// <summary>Typed sample at the clock position.</summary>
    public double GetCurrentValue(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return Evaluate(From ?? defaultOriginValue, ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0));
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

    /// <summary>
    /// Applies the timeline's acceleration/deceleration curve (cubic,
    /// C1-continuous, monotone; normalized when the ratios sum past 1).
    /// Matches the qualitative WPF contract, not bit-exact easing.
    /// </summary>
    internal double EaseProgress(double progress) => ApplyTimingCurve(progress);
}

/// <summary>
/// Storyboard (PresentationCore subset): runs child timelines against named
/// targets. v1 supports DoubleAnimation children, single-name target paths
/// (e.g. "Angle"), count/Forever repetition and AutoReverse. Key-frame
/// children stay a precise gap (Start fails fast naming them); key-frame
/// animations themselves evaluate through Animatable.BeginAnimation.
/// Completed comes from <see cref="Timeline"/> (real WPF declares it there).
/// </summary>
public class Storyboard : ParallelTimeline
{
    // Weak keys: entries die with the animation, never with the target.
    private static readonly ConditionalWeakTable<object, NameBox> Names = new();
    private static readonly ConditionalWeakTable<object, TargetBox> Targets = new();
    private static readonly ConditionalWeakTable<object, PropertyBox> Properties = new();

    private sealed class NameBox { public string Value = string.Empty; }
    private sealed class TargetBox { public object? Value; }
    private sealed class PropertyBox { public string Value = string.Empty; }

    public static void SetTargetName(object element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        Names.GetOrCreateValue(element).Value = value;
    }

    /// <summary>DependencyObject-typed TargetName setter (converted callers bind here).</summary>
    public static void SetTargetName(DependencyObject element, string value) =>
        SetTargetName((object)element, value);

    public static string GetTargetName(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Names.TryGetValue(element, out var name) ? name.Value : string.Empty;
    }

    /// <summary>DependencyObject-typed TargetName getter (converted callers bind here).</summary>
    public static string GetTargetName(DependencyObject element) => GetTargetName((object)element);

    public static void SetTarget(object element, object value)
    {
        ArgumentNullException.ThrowIfNull(element);
        Targets.GetOrCreateValue(element).Value = value;
    }

    /// <summary>DependencyObject-typed Target setter (converted callers bind here).</summary>
    public static void SetTarget(DependencyObject element, DependencyObject value) =>
        SetTarget((object)element, (object)value);

    public static object? GetTarget(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Targets.TryGetValue(element, out var target) ? target.Value : null;
    }

    /// <summary>DependencyObject-typed Target getter (converted callers bind here).</summary>
    public static object? GetTarget(DependencyObject element) => GetTarget((object)element);

    public static void SetTargetProperty(object element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        Properties.GetOrCreateValue(element).Value = value;
    }

    /// <summary>
    /// PropertyPath-typed TargetProperty setter (converted callers pass a
    /// real <see cref="PropertyPath"/>; only its single-name Path lands, the
    /// dotted-path boundary the pump enforces).
    /// </summary>
    public static void SetTargetProperty(DependencyObject element, PropertyPath value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        SetTargetProperty((object)element, value.Path);
    }

    public static string GetTargetProperty(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Properties.TryGetValue(element, out var property) ? property.Value : string.Empty;
    }

    /// <summary>DependencyObject-typed TargetProperty getter (converted callers bind here).</summary>
    public static string GetTargetProperty(DependencyObject element) => GetTargetProperty((object)element);

    /// <summary>Begins all children against the namescope rooted at this element.</summary>
    public void Begin() => Begin(null);

    /// <summary>
    /// Begins all children; TargetName entries resolve under
    /// <paramref name="containingObject"/> (null searches no namescope).
    /// </summary>
    public void Begin(FrameworkElement? containingObject) => WpfAnimationClock.Start(this, containingObject);

    /// <summary>Stops iteration; values already written stay in place.</summary>
    public void Stop() => WpfAnimationClock.Stop(this);

    /// <summary>
    /// Jumps the running clock to an offset (matches Storyboard.Seek): the
    /// board must be running; the entry re-anchors so the next pump samples
    /// from <paramref name="offset"/>.
    /// </summary>
    public void Seek(TimeSpan offset) => WpfAnimationClock.Seek(this, offset);

    /// <summary>
    /// Continues a running board (matches Storyboard.Resume). The engine
    /// pump never pauses a clock, so resuming a running board is identity;
    /// a stopped board has no clock to resume and fails fast.
    /// </summary>
    public void Resume() => WpfAnimationClock.Resume(this);
}

/// <summary>
/// Deterministic animation clock. The host pumps real frame deltas through
/// <see cref="PumpRealTime"/> (called from WasmWpfHost.Tick); tests pump
/// virtual deltas through <see cref="Advance"/>. Time only moves via those
/// entry points, so animation tests never depend on wall time.
/// </summary>
internal static class WpfAnimationClock
{
    private sealed class ActiveStoryboard
    {
        public required Storyboard Board;
        public required TimeSpan Start;
        public required IReadOnlyDictionary<AnimationTimeline, ResolvedTarget> Targets;
        public bool CompletedFired;
        public FrameworkElement? Scope;
        public string? BeginName;
    }

    private sealed class ResolvedTarget
    {
        public required object Instance;
        public required System.Reflection.PropertyInfo Property;
        public required double FromValue;
        public required double BaseValue;
        public object? BaseObject;

        /// <summary>Set once Step writes an evaluated value (see ActiveAnimation).</summary>
        public bool HasProducedValue;
    }

    private static TimeSpan _now;
    private static DateTime _lastReal = DateTime.UtcNow;
    private static readonly List<ActiveStoryboard> Active = new();
    private static readonly List<ActiveAnimation> Animations = new();

    internal static TimeSpan Now => _now;

    internal static void ResetForTests()
    {
        _now = TimeSpan.Zero;
        _lastReal = DateTime.UtcNow;
        Active.Clear();
        Animations.Clear();
    }

    internal static void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
        _now += delta;
        Pump();
    }

    internal static void PumpRealTime()
    {
        var utc = DateTime.UtcNow;
        var delta = utc - _lastReal;
        _lastReal = utc;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > TimeSpan.FromSeconds(1)) delta = TimeSpan.FromSeconds(1);
        Advance(delta);
    }

    internal static void Start(Storyboard board, FrameworkElement? scope, string? beginName = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        Stop(board);
        ResolveDeferredDurations(board, scope);
        ResolveDeferredKeyTimes(board, scope);
        ResolveDeferredTemplateBindings(board, scope);
        var targets = new Dictionary<AnimationTimeline, ResolvedTarget>();
        foreach (var child in board.Children)
        {
            if (child is not AnimationTimeline animation)
                throw new InvalidOperationException(
                    $"Storyboard only runs animation children in v1, not {child.GetType().Name}.");
            targets[animation] = Resolve(board, animation, scope);
        }
        Active.Add(new ActiveStoryboard { Board = board, Start = _now, Targets = targets, Scope = scope, BeginName = beginName });
    }

    internal static void Stop(Storyboard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        Active.RemoveAll(a => ReferenceEquals(a.Board, board));
    }

    /// <summary>Resolves template StaticResource key-frame times against the
    /// run scope (Expander ExpandKeyTime); missing keys keep Uniform, like
    /// other load-order leniencies, and resolve on a later run.</summary>
    /// <summary>Resolves template resource Duration references against the
    /// run scope (Expander ExpandDuration); missing keys keep Automatic.</summary>
    private static void ResolveDeferredDurations(Storyboard board, FrameworkElement? scope)
    {
        if (scope is null) return;
        ResolveDeferredDuration(board, scope);
        foreach (var child in board.Children)
            if (child is Timeline childTimeline)
                ResolveDeferredDuration(childTimeline, scope);
    }

    private static void ResolveDeferredDuration(Timeline timeline, FrameworkElement scope)
    {
        if (timeline.DeferredDurationKey is not { } key) return;
        object? found = scope.TryFindResource(key, out var resource) ? resource
            : ThemeDictionaries.TryFindThemeValue(scope, key, out var themed) ? themed : null;
        timeline.Duration = found switch
        {
            Duration duration => duration,
            TimeSpan timeSpan => new Duration(timeSpan),
            _ => timeline.Duration,
        };
    }

    /// <summary>Resolves template TemplateBinding animation endpoints
    /// against the templated parent at run (SmartHint HintOpacity To);
    /// missing values keep the endpoint null, like other load-order
    /// leniencies, and resolve on a later run.</summary>
    private static void ResolveDeferredTemplateBindings(Storyboard board, FrameworkElement? scope)
    {
        if (scope is null) return;
        var parent = scope.TemplateParent ?? scope;
        ResolveTemplateBindings(board, parent);
        foreach (var child in board.Children)
            if (child is Storyboard nested)
                ResolveTemplateBindings(nested, parent);
    }

    private static void ResolveTemplateBindings(Storyboard board, FrameworkElement parent)
    {
        foreach (var child in board.Children)
        {
            if (child is not DoubleAnimation animation) continue;
            if (animation.DeferredFromBinding is { } from && TryReadTemplateDouble(parent, from, out var fromValue))
                animation.From = fromValue;
            if (animation.DeferredToBinding is { } to && TryReadTemplateDouble(parent, to, out var toValue))
                animation.To = toValue;
            if (animation.DeferredByBinding is { } by && TryReadTemplateDouble(parent, by, out var byValue))
                animation.By = byValue;
        }
    }

    private static bool TryReadTemplateDouble(FrameworkElement parent, string path, out double value)
    {
        value = 0;
        if (!BindingExpression.TryRead(parent, path, out var raw) || raw is null) return false;
        if (raw is double direct) { value = direct; return true; }
        try { value = System.Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture); return true; }
        catch (Exception ex) when (ex is System.FormatException or System.InvalidCastException or System.OverflowException or System.ArgumentException)
        {
            return false;
        }
    }

    private static void ResolveDeferredKeyTimes(Storyboard board, FrameworkElement? scope)
    {
        if (scope is null) return;
        foreach (var child in board.Children)
        {
            if (child is not IKeyFrameAnimation keyed) continue;
            foreach (var frame in keyed.KeyFrames)
            {
                if (frame is not IKeyTimeDeferred deferred || deferred.DeferredKeyTimeKey is not { } key) continue;
                if (scope.TryFindResource(key, out var resource) && resource is KeyTime resolved)
                    deferred.KeyTime = resolved;
                else if (ThemeDictionaries.TryFindThemeValue(scope, key, out var themed) && themed is KeyTime themedTime)
                    deferred.KeyTime = themedTime;
            }
        }
    }

    /// <summary>Stops storyboards begun by a named BeginStoryboard action in
    /// a scope (matches RemoveStoryboard; unknown names are a silent no-op,
    /// like WPF).</summary>
    internal static void Remove(string beginName, FrameworkElement? scope)
    {
        Active.RemoveAll(a => string.Equals(a.BeginName, beginName, StringComparison.Ordinal) &&
            ReferenceEquals(a.Scope, scope));
    }

    internal static void Seek(Storyboard board, TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(board);
        var active = Active.FirstOrDefault(a => ReferenceEquals(a.Board, board))
            ?? throw new InvalidOperationException("Storyboard is not running; call Begin before Seek.");
        active.Start = _now - offset;
    }

    internal static void Resume(Storyboard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!Active.Any(a => ReferenceEquals(a.Board, board)))
            throw new InvalidOperationException("Storyboard is not running; call Begin before Resume.");
    }

    /// <summary>Resolves a dotted animation target path (attached segments,
    /// child indexers and CLR properties, like WPF property paths).</summary>
    private static (object Target, System.Reflection.PropertyInfo Property) WalkTargetPath(object root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        var segments = SplitTargetPath(path);
        if (segments.Count == 0)
            throw new InvalidOperationException($"Storyboard target path '{path}' is empty.");
        object current = root;
        for (var i = 0; i < segments.Count - 1; i++)
            current = WalkIntermediate(current, segments[i], path);
        return WalkFinal(current, segments[^1], path);
    }

    private static List<string> SplitTargetPath(string path)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == '.' && depth == 0)
            {
                parts.Add(path[start..i]);
                start = i + 1;
            }
        }
        parts.Add(path[start..]);
        return parts;
    }

    private static object WalkIntermediate(object current, string segment, string path)
    {
        var (name, indexes) = SplitIndexes(segment, path);
        var value = ReadSegmentValue(current, name, path);
        foreach (var index in indexes)
        {
            if (value is not System.Collections.IList list)
                throw new InvalidOperationException($"Storyboard target path '{path}' indexes a non-list '{name}'.");
            if (index < 0 || index >= list.Count)
                throw new InvalidOperationException($"Storyboard target path '{path}' index [{index}] is out of range.");
            value = list[index]
                ?? throw new InvalidOperationException($"Storyboard target path '{path}' index [{index}] is null.");
        }
        if (value.GetType().IsValueType)
            throw new InvalidOperationException($"Storyboard target path '{path}' crosses a value-type segment (value-type path segments stay a precise boundary).");
        return value;
    }

    private static (object Target, System.Reflection.PropertyInfo Property) WalkFinal(object current, string segment, string path)
    {
        var (name, indexes) = SplitIndexes(segment, path);
        if (indexes.Count > 0)
            throw new InvalidOperationException($"Storyboard target path '{path}' cannot animate an indexed element directly (target a double property).");
        var holder = current;
        var propName = name;
        if (name.StartsWith('(') && name.EndsWith(')'))
        {
            var inner = name[1..^1];
            var dot = inner.IndexOf('.');
            if (dot > 0) ResolvePathOwner(inner[..dot], path);
            propName = dot > 0 ? inner[(dot + 1)..] : inner;
        }
        if (holder.GetType().IsValueType)
            throw new InvalidOperationException($"Storyboard target path '{path}' targets a value-type holder (value-type animation targets stay a precise boundary).");
        var property = holder.GetType().GetProperty(propName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"'{propName}' is not a readable/writable property on {holder.GetType().Name} (animation target).");
        return (holder, property);
    }

    private static object ReadSegmentValue(object current, string name, string path)
    {
        if (name.StartsWith('(') && name.EndsWith(')'))
        {
            var inner = name[1..^1];
            var dot = inner.IndexOf('.');
            if (dot <= 0) return ReadSegmentValue(current, inner, path);
            var owner = ResolvePathOwner(inner[..dot], path);
            var propName = inner[(dot + 1)..];
            if (current is DependencyObject dependency &&
                DependencyProperty.FromName(owner, propName) is { } dp)
            {
                var stored = dependency.GetValue(dp)
                    ?? throw new InvalidOperationException($"Storyboard target path '{path}' passes through null '{name}'.");
                if (dp.DefaultValue is null || !Equals(stored, dp.DefaultValue))
                    return stored;
                // Stored value is the registration default (the DP was never
                // written): CLR-first writers (template/parse/style setters,
                // e.g. XAML-set UIElement.RenderTransform) land on the CLR
                // slot while the DP keeps its default, so read there before
                // reporting the default. Unified DPs read identically either
                // way; genuinely unset targets still fall through to the
                // default and fail downstream exactly as before.
                if (current.GetType().GetProperty(propName,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is { } clrProp &&
                    clrProp.CanRead && clrProp.GetValue(current) is { } clrValue)
                    return clrValue;
                return stored;
            }
            var clr = current.GetType().GetProperty(propName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"'{propName}' is not a readable property on {current.GetType().Name} (animation target '{path}').");
            if (!clr.CanRead)
                throw new InvalidOperationException(
                    $"'{propName}' is not a readable property on {current.GetType().Name} (animation target '{path}').");
            return clr.GetValue(current)
                ?? throw new InvalidOperationException($"Storyboard target path '{path}' passes through null '{name}'.");
        }
        var property = current.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"'{name}' is not a readable/writable property on {current.GetType().Name} (animation target '{path}').");
        if (!property.CanRead)
            throw new InvalidOperationException(
                $"'{name}' is not a readable/writable property on {current.GetType().Name} (animation target '{path}').");
        return property.GetValue(current)
            ?? throw new InvalidOperationException($"Storyboard target path '{path}' passes through null '{name}'.");
    }

    private static Type ResolvePathOwner(string ownerName, string path) =>
        XamlLoader.FindEngineType(ownerName) ?? XamlLoader.FindConvertedType(ownerName)
        ?? throw new InvalidOperationException($"Storyboard target path '{path}' names unknown type '{ownerName}'.");

    private static (string Name, List<int> Indexes) SplitIndexes(string segment, string path)
    {
        var indexes = new List<int>();
        var name = segment;
        while (name.EndsWith(']'))
        {
            var open = name.LastIndexOf('[');
            if (open < 0) throw new InvalidOperationException($"Storyboard target path '{path}' has a malformed indexer.");
            if (!int.TryParse(name[(open + 1)..^1], out var index))
                throw new InvalidOperationException($"Storyboard target path '{path}' has a non-integer indexer.");
            indexes.Insert(0, index);
            name = name[..open];
        }
        if (name.Length == 0) throw new InvalidOperationException($"Storyboard target path '{path}' has a malformed indexer.");
        return (name, indexes);
    }

    private static ResolvedTarget Resolve(Storyboard board, AnimationTimeline animation, FrameworkElement? scope)
    {
        var instance = GetTarget(board, animation, scope);
        var path = Storyboard.GetTargetProperty(animation);
        if (string.IsNullOrEmpty(path)) path = Storyboard.GetTargetProperty(board);
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException($"{animation.GetType().Name} needs Storyboard.TargetProperty (single property name in v1).");
        var (target, property) = path.Contains('.') || path.Contains('(') || path.Contains('[')
            ? WalkTargetPath(instance, path)
            : (instance, instance.GetType().GetProperty(path,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
              ?? throw new InvalidOperationException(
                $"'{path}' is not a readable/writable property on {instance.GetType().Name} (animation target)."));
        if (!property.CanRead || !property.CanWrite)
            throw new InvalidOperationException(
                $"'{path}' is not a readable/writable property on {target.GetType().Name} (animation target).");
        if (animation is ObjectAnimationUsingKeyFrames objectKeyed)
        {
            // String key-frame values convert against the endpoint now
            // (idempotent: converted values are no longer strings).
            foreach (var frame in objectKeyed.KeyFrames)
                if (frame.Value is string raw)
                    frame.Value = StyleValueConverter.Convert(raw, property.PropertyType)
                        ?? throw new InvalidOperationException(
                            $"'{raw}' does not convert to {property.PropertyType.Name} (animation target '{path}').");
        }
        else if (animation is BooleanAnimationUsingKeyFrames)
        {
            if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?))
                throw new InvalidOperationException(
                    $"'{path}' on {target.GetType().Name} is not a boolean property (animation target).");
        }
        else if (animation is ThicknessAnimationBase or ThicknessAnimation)
        {
            if (property.PropertyType != typeof(Thickness))
                throw new InvalidOperationException(
                    $"'{path}' on {target.GetType().Name} is not a thickness property (animation target).");
        }
        else if (property.PropertyType != typeof(double) && property.PropertyType != typeof(double?))
            throw new InvalidOperationException(
                $"'{path}' on {target.GetType().Name} is not a double property (animation target).");
        var current = property.GetValue(target);
        var baseValue = current is double d ? d : 0.0;
        return new ResolvedTarget
        {
            Instance = target,
            Property = property,
            FromValue = baseValue,
            BaseValue = baseValue,
            BaseObject = current,
        };
    }

    private static object GetTarget(Storyboard board, AnimationTimeline animation, FrameworkElement? scope)
    {
        var direct = Storyboard.GetTarget(animation) ?? Storyboard.GetTarget(board);
        if (direct is not null) return direct;
        var name = Storyboard.GetTargetName(animation);
        if (string.IsNullOrEmpty(name)) name = Storyboard.GetTargetName(board);
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("DoubleAnimation needs Storyboard.TargetName or Storyboard.Target (attach the storyboard, not the scope).");
        if (scope is null)
            throw new InvalidOperationException($"Animation target '{name}' needs a namescope: call Begin(containingObject).");
        return FindInNameScopes(scope, name)
            ?? throw new InvalidOperationException($"Animation target '{name}' was not found under {scope.GetType().Name}.");
    }

    private static object? FindInNameScopes(FrameworkElement scope, string name)
    {
        // Visual names first (code-built trees without a hydrated component).
        var found = FindByName(scope, name);
        if (found is not null) return found;
        // Then the template namescope (x:Name'd template parts, including
        // non-visuals like storyboard transform targets, which the visual
        // walk cannot reach).
        for (FrameworkElement? node = scope; node is not null; node = node.TemplateParent as FrameworkElement)
        {
            if (node is Control control && control.ActiveTemplateNames?.TryGetValue(name, out var named) == true)
                return named;
        }
        // Then the hydrated component namescopes up the ancestor chain, which
        // also cover 3D names the logical-tree walk cannot reach (e.g.
        // Geometry rotations on a page hosted in a navigation window).
        for (FrameworkElement? node = scope; node is not null; node = node.Parent as FrameworkElement)
            if (WpfAppBoot.NameScopes.TryGetValue(node, out var names) &&
                names.TryGetValue(name, out var registered))
                return registered;
        return null;
    }

    private static FrameworkElement? FindByName(FrameworkElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.LogicalChildren)
            if (child is FrameworkElement fe)
            {
                var found = FindByName(fe, name);
                if (found is not null) return found;
            }
        return null;
    }

    private static void Pump()
    {
        foreach (var active in Active.ToList())
        {
            var elapsed = _now - active.Start;
            var done = true;
            foreach (var (animation, target) in active.Targets)
            {
                if (!Step(animation, target, elapsed)) done = false;
            }
            if (done && !active.CompletedFired)
            {
                active.CompletedFired = true;
                Active.Remove(active);
                active.Board.RaiseCompleted();
            }
        }
        PumpAnimations();
    }

    private sealed class ActiveAnimation
    {
        public required DependencyObject Target;
        public required DependencyProperty Property;
        public required System.Reflection.PropertyInfo? ClrProperty;
        public required AnimationTimeline Timeline;
        public required object? BaseValue;
        public required TimeSpan Start;

        /// <summary>
        /// Set once the pump writes an evaluated (animated) value for this
        /// entry (DependencyPropertyHelper.IsAnimated surface): a registered
        /// but unticked clock reports false, live-verified against WPF.
        /// Handoff base restores and FillBehavior.Stop completions do not set
        /// it (they write the base value, not animated ink).
        /// </summary>
        public bool HasProducedValue;
    }

    /// <summary>
    /// Animation-liveness probe for DependencyPropertyHelper (internal):
    /// true while an entry for (target, dp) has produced an evaluated value
    /// and has not been removed. Storyboard double targets match by CLR
    /// property name (they write through reflection, like the engine pump).
    /// </summary>
    internal static bool HasProducedValue(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        foreach (var active in Animations)
            if (active.HasProducedValue && ReferenceEquals(active.Target, target) && active.Property == dp)
                return true;
        foreach (var board in Active)
            foreach (var resolved in board.Targets.Values)
                if (resolved.HasProducedValue && ReferenceEquals(resolved.Instance, target) &&
                    string.Equals(resolved.Property.Name, dp.Name, StringComparison.Ordinal))
                    return true;
        return false;
    }

    /// <summary>
    /// Starts (or, with a null timeline, removes) a property animation.
    /// SnapshotAndReplace restores any running animation's base value first;
    /// Compose keeps the current animated value as the new base, so a
    /// retarget never jumps. Fails fast on animation-prohibited properties,
    /// type mismatches and Paced key frames.
    /// </summary>
    internal static void BeginAnimation(
        DependencyObject target, DependencyProperty property, AnimationTimeline? timeline, HandoffBehavior handoff)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        var existing = Animations.FindIndex(a => ReferenceEquals(a.Target, target) && a.Property == property);
        if (existing >= 0)
        {
            var prior = Animations[existing];
            Animations.RemoveAt(existing);
            if (handoff == HandoffBehavior.SnapshotAndReplace)
                WriteAnimatedValue(prior, prior.BaseValue);
        }
        if (timeline is null) return;
        if (IsAnimationProhibited(target, property))
            throw new InvalidOperationException(
                $"'{property.Name}' on {target.GetType().Name} is animation-prohibited (IsAnimationProhibited).");
        var clr = target.GetType().GetProperty(property.Name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (clr is not null && (!clr.CanRead || !clr.CanWrite))
            clr = null;
        var valueType = clr?.PropertyType ?? property.PropertyType;
        if (timeline is ObjectAnimationUsingKeyFrames keys)
            foreach (var frame in keys.KeyFrames)
            {
                if (frame.KeyTime.Type == KeyTimeType.Paced)
                    throw new InvalidOperationException(
                        "KeyTime Paced needs distance-based pacing, which object key-frame evaluation does not implement.");
                if (frame.Value is not null && !valueType.IsInstanceOfType(frame.Value))
                    throw new InvalidOperationException(
                        $"Key-frame value '{frame.Value.GetType().Name}' does not match '{property.Name}' ('{valueType.Name}').");
            }
        if (timeline is DoubleAnimationUsingKeyFrames doubles)
            foreach (var frame in doubles.KeyFrames)
            {
                if (frame.KeyTime.Type == KeyTimeType.Paced)
                    throw new InvalidOperationException(
                        "KeyTime Paced needs distance-based pacing, which key-frame evaluation does not implement.");
                if (!valueType.IsInstanceOfType(frame.Value))
                    throw new InvalidOperationException(
                        $"Key-frame value '{frame.Value}' does not match '{property.Name}' ('{valueType.Name}').");
            }
        if (timeline.TargetPropertyType != typeof(object) &&
            valueType != timeline.TargetPropertyType &&
            !valueType.IsAssignableFrom(timeline.TargetPropertyType))
            throw new InvalidOperationException(
                $"'{timeline.GetType().Name}' targets '{timeline.TargetPropertyType.Name}' but '{property.Name}' is '{valueType.Name}'.");
        var current = clr?.GetValue(target) ?? target.GetValue(property);
        Animations.Add(new ActiveAnimation
        {
            Target = target,
            Property = property,
            ClrProperty = clr,
            Timeline = timeline,
            BaseValue = current,
            Start = _now,
        });
    }

    private static bool IsAnimationProhibited(DependencyObject target, DependencyProperty property)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
            if (DependencyProperty.TryGetOverride(type, property.Name, out var metadata) &&
                metadata is FrameworkPropertyMetadata framework && framework.IsAnimationProhibited)
                return true;
        return (property.Metadata as FrameworkPropertyMetadata)?.IsAnimationProhibited == true ||
            (property.Metadata as UIPropertyMetadata)?.IsAnimationProhibited == true;
    }

    private static void WriteAnimatedValue(ActiveAnimation active, object? value)
    {
        if (active.ClrProperty is not null)
            active.ClrProperty.SetValue(active.Target, value);
        else
            active.Target.SetValue(active.Property, value);
    }

    private static TimeSpan EffectiveDuration(AnimationTimeline timeline)
    {
        if (timeline.Duration.HasTimeSpan) return timeline.Duration.TimeSpan;
        if (timeline is ObjectAnimationUsingKeyFrames keys) return keys.GetNaturalDuration();
        if (timeline is DoubleAnimationUsingKeyFrames doubles) return doubles.GetNaturalDuration();
        if (timeline is PointAnimationUsingKeyFrames points) return points.GetNaturalDuration();
        if (timeline is BooleanAnimationUsingKeyFrames booleans) return booleans.GetNaturalDuration();
        if (timeline is ThicknessAnimationUsingKeyFrames thicknesses) return thicknesses.GetNaturalDuration();
        return TimeSpan.Zero;
    }

    private static void PumpAnimations()
    {
        foreach (var active in Animations.ToList())
        {
            var elapsed = _now - active.Start;
            var begin = active.Timeline.BeginTime ?? TimeSpan.Zero;
            if (elapsed < begin) continue;
            var local = elapsed - begin;
            var length = EffectiveDuration(active.Timeline);
            if (length <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, active.Timeline);
                active.HasProducedValue = true;
                WriteAnimatedValue(active, active.Timeline.GetCurrentValue(active.BaseValue!, active.BaseValue!, instant));
                Animations.Remove(active);
                continue;
            }
            var (progress, complete) = ProgressAt(active.Timeline, local, length);
            var clock = new AnimationClock(
                TimeSpan.FromTicks((long)(progress * length.Ticks)), progress, length, active.Timeline);
            if (complete && active.Timeline.FillBehavior == FillBehavior.Stop)
                WriteAnimatedValue(active, active.BaseValue);
            else
            {
                active.HasProducedValue = true;
                WriteAnimatedValue(active, active.Timeline.GetCurrentValue(active.BaseValue!, active.BaseValue!, clock));
            }
            if (complete) Animations.Remove(active);
        }
    }

    private static bool Step(AnimationTimeline animation, ResolvedTarget target, TimeSpan elapsed)
    {
        var begin = animation.BeginTime ?? TimeSpan.Zero;
        if (elapsed < begin) return false;
        var local = elapsed - begin;
        if (animation is ThicknessAnimationBase or ThicknessAnimation)
        {
            var origin = target.BaseObject is Thickness thickness ? thickness : new Thickness(0, 0, 0, 0);
            Thickness sample(Thickness from, AnimationClock clock) => animation switch
            {
                ThicknessAnimationUsingKeyFrames thicknessKeyed => thicknessKeyed.GetCurrentValue(from, from, clock),
                ThicknessAnimation single => single.GetCurrentValue(from, from, clock),
                _ => from,
            };
            var thicknessLength = EffectiveDuration(animation);
            if (thicknessLength <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, animation);
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, sample(origin, instant));
                return true;
            }
            var (thicknessProgress, thicknessComplete) = ProgressAt(animation, local, thicknessLength);
            var thicknessClock = new AnimationClock(TimeSpan.FromTicks((long)(thicknessProgress * thicknessLength.Ticks)), thicknessProgress, thicknessLength, animation);
            if (thicknessComplete && animation.FillBehavior == FillBehavior.Stop)
                target.Property.SetValue(target.Instance, target.BaseObject);
            else
            {
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, sample(origin, thicknessClock));
            }
            return thicknessComplete;
        }
        if (animation is PointAnimationUsingKeyFrames pointKeyed)
        {
            var pointLength = EffectiveDuration(pointKeyed);
            if (pointLength <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, pointKeyed);
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, pointKeyed.GetCurrentValue(target.FromValue, target.FromValue, instant));
                return true;
            }
            var (pointProgress, pointComplete) = ProgressAt(pointKeyed, local, pointLength);
            var pointClock = new AnimationClock(TimeSpan.FromTicks((long)(pointProgress * pointLength.Ticks)), pointProgress, pointLength, pointKeyed);
            if (pointComplete && pointKeyed.FillBehavior == FillBehavior.Stop)
                target.Property.SetValue(target.Instance, target.BaseValue);
            else
            {
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, pointKeyed.GetCurrentValue(target.FromValue, target.FromValue, pointClock));
            }
            return pointComplete;
        }
        if (animation is DoubleAnimationUsingKeyFrames keyed)
        {
            var keyLength = EffectiveDuration(keyed);
            if (keyLength <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, keyed);
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, keyed.GetCurrentValue(target.FromValue, target.FromValue, instant));
                return true;
            }
            var (keyProgress, keyComplete) = ProgressAt(keyed, local, keyLength);
            var keyClock = new AnimationClock(TimeSpan.FromTicks((long)(keyProgress * keyLength.Ticks)), keyProgress, keyLength, keyed);
            if (keyComplete && keyed.FillBehavior == FillBehavior.Stop)
                target.Property.SetValue(target.Instance, target.BaseValue);
            else
            {
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, keyed.GetCurrentValue(target.FromValue, target.FromValue, keyClock));
            }
            return keyComplete;
        }
        if (animation is BooleanAnimationUsingKeyFrames booleanKeyed)
        {
            var booleanLength = EffectiveDuration(booleanKeyed);
            if (booleanLength <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, booleanKeyed);
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, booleanKeyed.GetCurrentValue(ToBool(target.BaseObject), ToBool(target.BaseObject), instant));
                return true;
            }
            var (booleanProgress, booleanComplete) = ProgressAt(booleanKeyed, local, booleanLength);
            var booleanClock = new AnimationClock(TimeSpan.FromTicks((long)(booleanProgress * booleanLength.Ticks)), booleanProgress, booleanLength, booleanKeyed);
            if (booleanComplete && booleanKeyed.FillBehavior == FillBehavior.Stop)
                target.Property.SetValue(target.Instance, target.BaseObject);
            else
            {
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, booleanKeyed.GetCurrentValue(ToBool(target.BaseObject), ToBool(target.BaseObject), booleanClock));
            }
            return booleanComplete;
        }
        if (animation is ObjectAnimationUsingKeyFrames objectKeyed)
        {
            var objectLength = EffectiveDuration(objectKeyed);
            if (objectLength <= TimeSpan.Zero)
            {
                var instant = new AnimationClock(TimeSpan.Zero, 1.0, TimeSpan.Zero, objectKeyed);
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, objectKeyed.GetCurrentValue(target.FromValue, target.FromValue, instant));
                return true;
            }
            var (objectProgress, objectComplete) = ProgressAt(objectKeyed, local, objectLength);
            var objectClock = new AnimationClock(TimeSpan.FromTicks((long)(objectProgress * objectLength.Ticks)), objectProgress, objectLength, objectKeyed);
            if (objectComplete && objectKeyed.FillBehavior == FillBehavior.Stop)
                target.Property.SetValue(target.Instance, target.BaseValue);
            else
            {
                target.HasProducedValue = true;
                target.Property.SetValue(target.Instance, objectKeyed.GetCurrentValue(target.FromValue, target.FromValue, objectClock));
            }
            return objectComplete;
        }
        if (!animation.Duration.HasTimeSpan || animation.Duration.TimeSpan <= TimeSpan.Zero)
        {
            // Automatic (or zero) resolves to a single instant step for DoubleAnimation.
            target.Property.SetValue(target.Instance, ((DoubleAnimation)animation).Evaluate(target.FromValue, 1.0));
            return true;
        }
        var length = animation.Duration.TimeSpan;
        var (progress, complete) = ProgressAt(animation, local, length);
        if (complete && animation.FillBehavior == FillBehavior.Stop)
            target.Property.SetValue(target.Instance, target.BaseValue);
        else
        {
            target.HasProducedValue = true;
            target.Property.SetValue(target.Instance, ((DoubleAnimation)animation).Evaluate(target.FromValue, ((DoubleAnimation)animation).EaseProgress(progress)));
        }
        return complete;
    }

    private static bool ToBool(object? value) => value is bool b ? b : false;

    private static (double Progress, bool Complete) ProgressAt(Timeline animation, TimeSpan local, TimeSpan length)
    {
        if (animation.RepeatBehavior == RepeatBehavior.Forever)
            return (CycleAt(animation, local, length), false);
        if (animation.RepeatBehavior.RepeatDuration is { } total)
        {
            // A duration cap freezes the in-flight value (no HoldEnd edge).
            if (local >= total) return (CycleAt(animation, total, length), true);
            return (CycleAt(animation, local, length), false);
        }
        var count = animation.RepeatBehavior.Count ?? 1;
        if (local.Ticks >= (long)(length.Ticks * count))
        {
            // HoldEnd: the exact final edge of the last pass (reversed edge
            // when it ran backwards; partial last pass for fractional counts).
            return (FinalEdge(animation, count), true);
        }
        return (CycleAt(animation, local, length), false);
    }

    private static double FinalEdge(Timeline animation, double count)
    {
        var whole = Math.Floor(count);
        var frac = count - whole;
        if (frac > 0)
        {
            // Partial last pass: exact fractional position within it.
            var pass = (long)whole;
            var within = animation.AutoReverse && (pass % 2 == 1) ? 1.0 - frac : frac;
            return within;
        }
        var last = (long)whole - 1;
        return animation.AutoReverse && (last % 2 == 1) ? 0.0 : 1.0;
    }

    private static double CycleAt(Timeline animation, TimeSpan local, TimeSpan length)
    {
        var pass = (long)(local.TotalSeconds / length.TotalSeconds);
        var within = (local.TotalSeconds - pass * length.TotalSeconds) / length.TotalSeconds;
        within = Math.Clamp(within, 0.0, 1.0);
        return animation.AutoReverse && (pass % 2 == 1) ? 1.0 - within : within;
    }
}
