using System.Collections;
using System.Globalization;

namespace DesktopWasm.Wpf;

// Slice-81 animation spline/clock surface (PresentationCore subset, shapes
// live-verified against the 10.0.13 WindowsDesktop ref pack plus a live
// net10.0-windows runtime probe): KeySpline with real cubic-Bezier progress,
// the IKeyFrameAnimation contract, and a controllable Clock state machine
// with TimeSeekOrigin/ClockController (ClockState lives in
// Slice72Animation.cs with identical verified values).
//
// Retained limits: the deterministic WpfAnimationClock pump samples
// AnimationClock values, not Clock trees, so a Clock only advances through
// the explicit internal Advance step (state transitions driven by its
// Controller are real and pinned); Parent stays null (no clock tree),
// SeekAlignedToLastTick lands like Seek (no frame ticks), and repeats stay
// single-iteration like the pump's IsCumulative limit.

/// <summary>Key-frame contract (matches System.Windows.Media.Animation.IKeyFrameAnimation).</summary>
public interface IKeyFrameAnimation
{
    /// <summary>Frames driving the animation (boxed key-frame list).</summary>
    IList KeyFrames { get; set; }
}

/// <summary>Seek origin (matches TimeSeekOrigin; BeginTime=0/Duration=1, live-verified).</summary>
public enum TimeSeekOrigin
{
    BeginTime,
    Duration,
}

/// <summary>
/// Cubic-Bezier spline easing (matches System.Windows.Media.Animation.KeySpline,
/// IFormattable): control points default to (0,0)/(1,1). GetSplineProgress runs
/// the real WPF clamped Newton-Raphson solve (ported from dotnet/wpf
/// KeySpline.cs: accuracy .001, fuzz .000001, persistent parameter, cached
/// coefficients): the linear shape returns its input unchanged (even outside
/// 0..1, live-verified); time 0/1 land exactly; other inputs converge to the
/// 1e-3-scaled tolerance (live net10.0-windows probe values pinned in tests).
/// Only the X coordinate is validated (0..1, like WPF); Y is unconstrained.
/// </summary>
public sealed class KeySpline : Freezable, IFormattable
{
    private Point _controlPoint1 = new(0, 0);
    private Point _controlPoint2 = new(1, 1);
    private bool _isSpecified;
    private bool _isDirty = true;
    private double _parameter;
    private double _bx;
    private double _cx;
    private double _cx_Bx;
    private double _three_Cx;
    private double _by;
    private double _cy;

    private const double Accuracy = .001;
    private const double Fuzz = .000001;

    /// <summary>Linear spline (control points (0,0) and (1,1), like WPF).</summary>
    public KeySpline()
    {
    }

    /// <summary>Spline through two control points (live-verified shape).</summary>
    public KeySpline(Point controlPoint1, Point controlPoint2)
    {
        if (!IsValidControlPoint(controlPoint1))
            throw new ArgumentException($"'{nameof(controlPoint1)}' has an invalid value '{controlPoint1}'.", nameof(controlPoint1));
        if (!IsValidControlPoint(controlPoint2))
            throw new ArgumentException($"'{nameof(controlPoint2)}' has an invalid value '{controlPoint2}'.", nameof(controlPoint2));
        _controlPoint1 = controlPoint1;
        _controlPoint2 = controlPoint2;
    }

    /// <summary>Spline through two coordinate pairs (live-verified shape).</summary>
    public KeySpline(double x1, double y1, double x2, double y2)
        : this(new Point(x1, y1), new Point(x2, y2))
    {
    }

    /// <summary>First control point (default (0,0); X must stay in 0..1).</summary>
    public Point ControlPoint1
    {
        get => _controlPoint1;
        set
        {
            if (value != _controlPoint1)
            {
                if (!IsValidControlPoint(value))
                    throw new ArgumentException($"'{nameof(ControlPoint1)}' has an invalid value '{value}'.", nameof(value));
                ThrowIfFrozen();
                _controlPoint1 = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>Second control point (default (1,1); X must stay in 0..1).</summary>
    public Point ControlPoint2
    {
        get => _controlPoint2;
        set
        {
            if (value != _controlPoint2)
            {
                if (!IsValidControlPoint(value))
                    throw new ArgumentException($"'{nameof(ControlPoint2)}' has an invalid value '{value}'.", nameof(value));
                ThrowIfFrozen();
                _controlPoint2 = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Spline progress for a linear progress value (the real WPF solve, so
    /// results match desktop to the solver tolerance, not to machine epsilon).
    /// </summary>
    public double GetSplineProgress(double linearProgress)
    {
        if (_isDirty) Build();
        if (!_isSpecified) return linearProgress;
        SetParameterFromX(linearProgress);
        return GetBezierValue(_by, _cy, _parameter);
    }

    private static bool IsValidControlPoint(Point point) => point.X >= 0.0 && point.X <= 1.0;

    private void Build()
    {
        if (_controlPoint1 == new Point(0, 0) && _controlPoint2 == new Point(1, 1))
        {
            _isSpecified = false;
        }
        else
        {
            _isSpecified = true;
            _parameter = 0;
            _bx = 3 * _controlPoint1.X;
            _cx = 3 * _controlPoint2.X;
            _cx_Bx = 2 * (_cx - _bx);
            _three_Cx = 3 - _cx;
            _by = 3 * _controlPoint1.Y;
            _cy = 3 * _controlPoint2.Y;
        }
        _isDirty = false;
    }

    private static double GetBezierValue(double b, double c, double t)
    {
        var s = 1.0 - t;
        var t2 = t * t;
        return b * t * s * s + c * t2 * s + t2 * t;
    }

    private void GetXAndDx(double t, out double x, out double dx)
    {
        var s = 1.0 - t;
        var t2 = t * t;
        var s2 = s * s;
        x = _bx * t * s2 + _cx * t2 * s + t2 * t;
        dx = _bx * s2 + _cx_Bx * s * t + _three_Cx * t2;
    }

    private void SetParameterFromX(double time)
    {
        var bottom = 0.0;
        var top = 1.0;
        if (time == 0)
        {
            _parameter = 0;
        }
        else if (time == 1)
        {
            _parameter = 1;
        }
        else
        {
            while (top - bottom > Fuzz)
            {
                GetXAndDx(_parameter, out var x, out var dx);
                var absdx = Math.Abs(dx);
                if (x > time) top = _parameter;
                else bottom = _parameter;
                if (Math.Abs(x - time) < Accuracy * absdx) break;
                if (absdx > Fuzz)
                {
                    var next = _parameter - ((x - time) / dx);
                    if (next >= top) _parameter = (_parameter + top) / 2;
                    else if (next <= bottom) _parameter = (_parameter + bottom) / 2;
                    else _parameter = next;
                }
                else
                {
                    _parameter = (bottom + top) / 2;
                }
            }
        }
    }

    /// <summary>Parses the XAML KeySpline form ("x1,y1 x2,y2", like the native converter).</summary>
    public static KeySpline Parse(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var tokens = value.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 4 && tokens.All(n => double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            return new KeySpline(
                double.Parse(tokens[0], CultureInfo.InvariantCulture),
                double.Parse(tokens[1], CultureInfo.InvariantCulture),
                double.Parse(tokens[2], CultureInfo.InvariantCulture),
                double.Parse(tokens[3], CultureInfo.InvariantCulture));
        throw new FormatException($"'{value}' is not a key spline (x1,y1 x2,y2).");
    }

    /// <inheritdoc />
    public override string ToString() => $"{ControlPoint1},{ControlPoint2}";

    /// <summary>Invariant list form (matches Point's invariant formatting).</summary>
    public string ToString(string? format, IFormatProvider? provider) => ToString();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new KeySpline();
}

/// <summary>
/// Animation clock (matches System.Windows.Media.Animation.Clock :
/// DispatcherObject): a controllable single-iteration state machine over an
/// optional Timeline duration. Starts Stopped (null reads); the Controller
/// drives Begin/Pause/Resume/Seek/SkipToFill/Stop/Remove with real transition
/// rules, and Advance moves Active time forward (the deterministic animation
/// pump does not drive Clock trees; Advance is the engine's explicit step).
/// Converted readers (e.g. HandyControl's GeometryAnimation GetCurrentValue
/// overrides reading CurrentTime/CurrentState/CurrentProgress/
/// CurrentIteration) bind the exact WPF read surface.
/// </summary>
public class Clock : DispatcherObject
{
    private TimeSpan? _time;
    private double? _progress;
    private int? _iteration;
    private ClockState _state = ClockState.Stopped;
    private ClockController? _controller;

    /// <summary>Clock over a timeline (matches the protected-internal WPF ctor).</summary>
    protected internal Clock(Timeline? timeline) => Timeline = timeline;

    /// <summary>Test/diagnostic clock (the engine builds live clocks off timelines).</summary>
    internal static Clock CreateForTests(Timeline? timeline = null) => new TestClock(timeline);

    private sealed class TestClock : Clock
    {
        public TestClock(Timeline? timeline) : base(timeline)
        {
        }
    }

    /// <summary>Controller driving this clock (created on first read, like a controllable root).</summary>
    public ClockController Controller => _controller ??= new ClockController(this);

    /// <summary>Current speed multiplier (null when stopped; 0 when paused).</summary>
    public double? CurrentGlobalSpeed => _state == ClockState.Stopped ? null : IsPaused ? 0.0 : Controller.SpeedRatio;

    /// <summary>Iteration time within the current iteration (null when stopped).</summary>
    public TimeSpan? CurrentTime => _time;

    /// <summary>Direction-adjusted 0..1 progress (null when stopped or durationless).</summary>
    public double? CurrentProgress => _progress;

    /// <summary>Current iteration (1-based once begun; null when stopped).</summary>
    public int? CurrentIteration => _iteration;

    /// <summary>Lifetime state (Stopped until Begin runs).</summary>
    public ClockState CurrentState => _state;

    /// <summary>True while paused (state stays Active, speed reads 0).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>No clock tree in the engine (always null, documented).</summary>
    public Clock? Parent => null;

    /// <summary>Own controller is controllable (always true).</summary>
    public bool HasControllableRoot => true;

    /// <summary>Timeline this clock samples.</summary>
    public Timeline? Timeline { get; }

    /// <summary>Timeline duration, or Automatic when durationless.</summary>
    public Duration NaturalDuration => Timeline?.Duration ?? Duration.Automatic;

    /// <summary>Global time (no tree: the local time, or zero when stopped).</summary>
    protected TimeSpan CurrentGlobalTime => _time ?? TimeSpan.Zero;

    /// <summary>Raised when the clock completes (SkipToFill or reaching the end).</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the effective speed changes (pause/resume/ratio while live).</summary>
    public event EventHandler? CurrentGlobalSpeedInvalidated;

    /// <summary>Raised on every Active/Filling/Stopped transition.</summary>
    public event EventHandler? CurrentStateInvalidated;

    /// <summary>Raised whenever the iteration time changes.</summary>
    public event EventHandler? CurrentTimeInvalidated;

    /// <summary>Raised when the clock is removed.</summary>
    public event EventHandler? RemoveRequested;

    /// <summary>
    /// Explicit engine step moving Active time forward (the deterministic
    /// pump samples AnimationClock values instead; nothing in the engine
    /// calls this behind converted code).
    /// </summary>
    internal void Advance(TimeSpan delta)
    {
        if (_state != ClockState.Active || IsPaused || delta <= TimeSpan.Zero) return;
        var scaled = TimeSpan.FromTicks((long)(delta.Ticks * Controller.SpeedRatio));
        SetTime((_time ?? TimeSpan.Zero) + scaled, fromAdvance: true);
    }

    private void BeginCore()
    {
        IsPaused = false;
        SetState(ClockState.Active);
        _iteration = 1;
        SetTime(TimeSpan.Zero, fromAdvance: false);
        if (!DurationSpan().HasValue) _progress = null;
    }

    private TimeSpan? DurationSpan()
    {
        var duration = Timeline?.Duration ?? Duration.Automatic;
        return duration.HasTimeSpan ? duration.TimeSpan : null;
    }

    private void SetTime(TimeSpan time, bool fromAdvance)
    {
        var duration = DurationSpan();
        if (duration.HasValue && time >= duration.Value)
        {
            _time = duration.Value;
            _progress = 1.0;
            CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);
            if (_state == ClockState.Active)
            {
                SetState(ClockState.Filling);
                Completed?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        _time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        _progress = duration.HasValue && duration.Value > TimeSpan.Zero
            ? _time.Value.Ticks / (double)duration.Value.Ticks
            : null;
        CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);
        _ = fromAdvance;
    }

    private void SetState(ClockState state)
    {
        if (_state == state) return;
        _state = state;
        CurrentStateInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void StopCore(bool raiseRemove)
    {
        IsPaused = false;
        _time = null;
        _progress = null;
        _iteration = null;
        if (raiseRemove) RemoveRequested?.Invoke(this, EventArgs.Empty);
        SetState(ClockState.Stopped);
        CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);
    }

    internal void BeginFromController() => BeginCore();

    internal void PauseFromController()
    {
        if (_state != ClockState.Active || IsPaused) return;
        IsPaused = true;
        CurrentGlobalSpeedInvalidated?.Invoke(this, EventArgs.Empty);
        CurrentStateInvalidated?.Invoke(this, EventArgs.Empty);
    }

    internal void ResumeFromController()
    {
        if (_state != ClockState.Active || !IsPaused) return;
        IsPaused = false;
        CurrentGlobalSpeedInvalidated?.Invoke(this, EventArgs.Empty);
        CurrentStateInvalidated?.Invoke(this, EventArgs.Empty);
    }

    internal void SeekFromController(TimeSpan offset, TimeSeekOrigin origin)
    {
        // Seeking a stopped clock has no effect (documented engine rule).
        if (_state == ClockState.Stopped) return;
        var duration = DurationSpan();
        TimeSpan target;
        if (origin == TimeSeekOrigin.Duration)
        {
            if (!duration.HasValue) return;
            target = duration.Value - offset;
        }
        else
        {
            target = offset;
        }
        SetTime(target, fromAdvance: false);
    }

    internal void SkipToFillFromController()
    {
        if (_state == ClockState.Stopped) return;
        var duration = DurationSpan();
        _time = duration ?? _time ?? TimeSpan.Zero;
        _progress = 1.0;
        CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);
        SetState(ClockState.Filling);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    internal void StopFromController() => StopCore(raiseRemove: false);

    internal void RemoveFromController() => StopCore(raiseRemove: true);
}

/// <summary>
/// Clock controller (matches System.Windows.Media.Animation.ClockController):
/// the SpeedRatio knob plus the transition verbs, all applied to the owning
/// clock with the engine's documented single-iteration rules.
/// </summary>
public sealed class ClockController : DispatcherObject
{
    private readonly Clock _clock;
    private double _speedRatio = 1.0;

    internal ClockController(Clock clock) => _clock = clock;

    /// <summary>Owning clock.</summary>
    public Clock Clock => _clock;

    /// <summary>Speed multiplier (finite, non-negative; default 1).</summary>
    public double SpeedRatio
    {
        get => _speedRatio;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "SpeedRatio must be finite and non-negative.");
            _speedRatio = value;
        }
    }

    /// <summary>Restarts the clock (Active, time zero, iteration 1).</summary>
    public void Begin() => _clock.BeginFromController();

    /// <summary>Pauses a live clock (state stays Active, speed reads 0).</summary>
    public void Pause() => _clock.PauseFromController();

    /// <summary>Resumes a paused clock.</summary>
    public void Resume() => _clock.ResumeFromController();

    /// <summary>Moves the iteration time (no-op when stopped or durationless-for-Duration).</summary>
    public void Seek(TimeSpan offset, TimeSeekOrigin origin) => _clock.SeekFromController(offset, origin);

    /// <summary>Frame-aligned seek (no frame ticks in the engine: lands like Seek).</summary>
    public void SeekAlignedToLastTick(TimeSpan offset, TimeSeekOrigin origin) => _clock.SeekFromController(offset, origin);

    /// <summary>Completes the clock into its fill values (raises Completed).</summary>
    public void SkipToFill() => _clock.SkipToFillFromController();

    /// <summary>Stops the clock (Stopped, null reads, no Completed).</summary>
    public void Stop() => _clock.StopFromController();

    /// <summary>Removes the clock (Stopped plus RemoveRequested).</summary>
    public void Remove() => _clock.RemoveFromController();
}
