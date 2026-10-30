using System.Collections;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-81 KeySpline/IKeyFrameAnimation/Clock regression tests: real
/// cubic-Bezier progress pinned against a live net10.0-windows probe, the
/// explicit key-frame contract on both UsingKeyFrames animations, and the
/// controllable Clock state machine (defaults, Controller transitions,
/// event pins). No Application state is touched, so these stay outside the
/// serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice81SplineClockTests
{
    // KeySpline runs the real WPF clamped Newton-Raphson solve (ported
    // operation-for-operation from dotnet/wpf), so progress values match the
    // live net10.0-windows probe bit-for-bit from the same call sequence.
    private const double Tolerance = 0;

    [Fact]
    public void KeySpline_defaults_to_linear_identity()
    {
        var spline = new KeySpline();
        Assert.Equal(new Point(0, 0), spline.ControlPoint1);
        Assert.Equal(new Point(1, 1), spline.ControlPoint2);
        Assert.Equal("0,0,1,1", spline.ToString());
        Assert.Equal(-0.5, spline.GetSplineProgress(-0.5));
        Assert.Equal(0.0, spline.GetSplineProgress(0.0));
        Assert.Equal(0.33, spline.GetSplineProgress(0.33));
        Assert.Equal(1.0, spline.GetSplineProgress(1.0));
        Assert.Equal(1.5, spline.GetSplineProgress(1.5));
    }

    [Fact]
    public void KeySpline_ctors_agree()
    {
        var viaPoints = new KeySpline(new Point(0.5, 0), new Point(0.5, 1));
        var viaDoubles = new KeySpline(0.5, 0, 0.5, 1);
        Assert.Equal(viaPoints.ControlPoint1, viaDoubles.ControlPoint1);
        Assert.Equal(viaPoints.ControlPoint2, viaDoubles.ControlPoint2);
        Assert.Equal(viaPoints.GetSplineProgress(0.25), viaDoubles.GetSplineProgress(0.25));
    }

    [Fact]
    public void KeySpline_control_points_round_trip()
    {
        var spline = new KeySpline();
        spline.ControlPoint1 = new Point(0.9, 0.1);
        spline.ControlPoint2 = new Point(0.1, 0.9);
        Assert.Equal(new Point(0.9, 0.1), spline.ControlPoint1);
        Assert.Equal(new Point(0.1, 0.9), spline.ControlPoint2);
    }

    [Fact]
    public void KeySpline_rejects_out_of_range_x()
    {
        // Only X is validated (Y is unconstrained), like WPF.
        Assert.Throws<ArgumentException>(() => new KeySpline(new Point(-0.1, 0), new Point(1, 1)));
        Assert.Throws<ArgumentException>(() => new KeySpline(new Point(0, 0), new Point(1.1, 1)));
        Assert.Throws<ArgumentException>(() => new KeySpline(-0.1, 0, 1, 1));
        var ok = new KeySpline(0, -5, 1, 1e9);
        Assert.Equal(new Point(0, -5), ok.ControlPoint1);
        var spline = new KeySpline();
        Assert.Throws<ArgumentException>(() => spline.ControlPoint1 = new Point(2, 0.5));
        Assert.Throws<ArgumentException>(() => spline.ControlPoint2 = new Point(1.5, -1.5));
        spline.ControlPoint2 = new Point(0.5, -3.25);
        Assert.Equal(new Point(0.5, -3.25), spline.ControlPoint2);
        spline.Freeze();
        Assert.Throws<InvalidOperationException>(() => spline.ControlPoint1 = new Point(0.1, 0.1));
    }

    [Fact]
    public void KeySpline_progress_matches_live_wpf_ease()
    {
        // Live net10.0-windows probe pins (KeySpline(0.5, 0, 0.5, 1)).
        var spline = new KeySpline(0.5, 0, 0.5, 1);
        Assert.Equal(0.0, spline.GetSplineProgress(-0.5));
        Assert.Equal(0.0, spline.GetSplineProgress(0.0));
        Assert.Equal(0.01461255121512868, spline.GetSplineProgress(0.1), Tolerance);
        Assert.Equal(0.10569718975782497, spline.GetSplineProgress(0.25), Tolerance);
        Assert.Equal(0.49943797297703996, spline.GetSplineProgress(0.5), Tolerance);
        Assert.Equal(0.8941085045799866, spline.GetSplineProgress(0.75), Tolerance);
        Assert.Equal(0.9854927825050837, spline.GetSplineProgress(0.9), Tolerance);
        Assert.Equal(1.0, spline.GetSplineProgress(1.0));
        Assert.Equal(1.0, spline.GetSplineProgress(1.5));
    }

    [Fact]
    public void KeySpline_progress_matches_live_wpf_steep()
    {
        // Live net10.0-windows probe pins (KeySpline(0.9, 0.1, 0.1, 0.9)).
        var spline = new KeySpline(new Point(0.9, 0.1), new Point(0.1, 0.9));
        Assert.Equal(0.0, spline.GetSplineProgress(-0.5));
        Assert.Equal(0.015250862337718742, spline.GetSplineProgress(0.1), Tolerance);
        Assert.Equal(0.06071754147694828, spline.GetSplineProgress(0.25), Tolerance);
        Assert.Equal(0.49999864336287175, spline.GetSplineProgress(0.5), Tolerance);
        Assert.Equal(0.9392170535639948, spline.GetSplineProgress(0.75), Tolerance);
        Assert.Equal(0.9849315968265457, spline.GetSplineProgress(0.9), Tolerance);
        Assert.Equal(1.0, spline.GetSplineProgress(1.0));
        Assert.Equal(1.0, spline.GetSplineProgress(1.5));
    }

    [Fact]
    public void KeySpline_progress_is_monotone()
    {
        var spline = new KeySpline(0.5, 0, 0.5, 1);
        var previous = double.NegativeInfinity;
        for (var i = 0; i <= 100; i++)
        {
            var current = spline.GetSplineProgress(i / 100.0);
            Assert.True(current >= previous, $"non-monotone at {i}/100");
            previous = current;
        }
    }

    [Fact]
    public void KeySpline_clone_carries_control_points()
    {
        var spline = new KeySpline(0.2, 0.3, 0.7, 0.8);
        var clone = Assert.IsType<KeySpline>(spline.Clone());
        Assert.NotSame(spline, clone);
        Assert.Equal(spline.ControlPoint1, clone.ControlPoint1);
        Assert.Equal(spline.ControlPoint2, clone.ControlPoint2);
        clone.ControlPoint1 = new Point(0, 0);
        Assert.Equal(new Point(0.2, 0.3), spline.ControlPoint1);
    }

    [Fact]
    public void Double_animation_implements_key_frame_animation()
    {
        IKeyFrameAnimation animation = new DoubleAnimationUsingKeyFrames();
        Assert.Empty(animation.KeyFrames);
        animation.KeyFrames = new ArrayList
        {
            new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))),
            new DiscreteDoubleKeyFrame(2.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))),
        };
        var typed = Assert.IsType<DoubleAnimationUsingKeyFrames>(animation);
        Assert.Equal(2, typed.KeyFrames.Count);
        Assert.Same(typed.KeyFrames, animation.KeyFrames);
        Assert.Throws<ArgumentException>(() => animation.KeyFrames = new ArrayList { new DiscreteObjectKeyFrame("x") });
        Assert.Throws<ArgumentNullException>(() => animation.KeyFrames = null!);
    }

    [Fact]
    public void Object_animation_implements_key_frame_animation()
    {
        IKeyFrameAnimation animation = new ObjectAnimationUsingKeyFrames();
        Assert.Empty(animation.KeyFrames);
        animation.KeyFrames = new ArrayList { new DiscreteObjectKeyFrame("a"), new DiscreteObjectKeyFrame("b") };
        var typed = Assert.IsType<ObjectAnimationUsingKeyFrames>(animation);
        Assert.Equal(2, typed.KeyFrames.Count);
        Assert.Equal("b", typed.KeyFrames[1].Value);
        Assert.Throws<ArgumentException>(() => animation.KeyFrames = new ArrayList { new LinearDoubleKeyFrame(1.0) });
    }

    [Fact]
    public void Clock_starts_stopped_with_null_reads()
    {
        var clock = Clock.CreateForTests();
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Null(clock.CurrentTime);
        Assert.Null(clock.CurrentProgress);
        Assert.Null(clock.CurrentIteration);
        Assert.Null(clock.CurrentGlobalSpeed);
        Assert.False(clock.IsPaused);
        Assert.Null(clock.Parent);
        Assert.True(clock.HasControllableRoot);
        Assert.Equal(Duration.Automatic, clock.NaturalDuration);
        Assert.Same(clock, clock.Controller.Clock);
        Assert.Same(clock.Controller, clock.Controller);
        Assert.Equal(1.0, clock.Controller.SpeedRatio);
    }

    [Fact]
    public void Clock_reports_timeline_duration()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        Assert.Equal(new Duration(TimeSpan.FromSeconds(2)), clock.NaturalDuration);
        Assert.Same(timeline, clock.Timeline);
    }

    [Fact]
    public void Clock_begin_starts_iteration()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        var states = 0;
        var times = 0;
        clock.CurrentStateInvalidated += (_, _) => states++;
        clock.CurrentTimeInvalidated += (_, _) => times++;
        var completed = 0;
        clock.Completed += (_, _) => completed++;
        clock.Controller.Begin();
        Assert.Equal(ClockState.Active, clock.CurrentState);
        Assert.Equal(TimeSpan.Zero, clock.CurrentTime);
        Assert.Equal(0.0, clock.CurrentProgress);
        Assert.Equal(1, clock.CurrentIteration);
        Assert.Equal(1, states);
        Assert.Equal(1, times);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Clock_advance_reaches_fill_and_completes()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        var completed = 0;
        clock.Completed += (_, _) => completed++;
        clock.Controller.Begin();
        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(0.5), clock.CurrentTime);
        Assert.Equal(0.25, Assert.IsType<double>(clock.CurrentProgress), Tolerance);
        Assert.Equal(ClockState.Active, clock.CurrentState);
        Assert.Equal(0, completed);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(ClockState.Filling, clock.CurrentState);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.CurrentTime);
        Assert.Equal(1.0, clock.CurrentProgress);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Clock_pause_and_resume_gate_speed()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        var speedChanges = 0;
        clock.CurrentGlobalSpeedInvalidated += (_, _) => speedChanges++;
        clock.Controller.Begin();
        clock.Advance(TimeSpan.FromSeconds(0.5));
        clock.Controller.Pause();
        Assert.True(clock.IsPaused);
        Assert.Equal(ClockState.Active, clock.CurrentState);
        Assert.Equal(0.0, clock.CurrentGlobalSpeed);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(0.5), clock.CurrentTime);
        clock.Controller.Resume();
        Assert.False(clock.IsPaused);
        Assert.Equal(1.0, clock.CurrentGlobalSpeed);
        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.CurrentTime);
        Assert.Equal(2, speedChanges);
    }

    [Fact]
    public void Clock_seek_moves_iteration_time()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        // Seeking a stopped clock has no effect.
        clock.Controller.Seek(TimeSpan.FromSeconds(1), TimeSeekOrigin.BeginTime);
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Null(clock.CurrentTime);
        clock.Controller.Begin();
        clock.Controller.Seek(TimeSpan.FromSeconds(1), TimeSeekOrigin.BeginTime);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.CurrentTime);
        Assert.Equal(0.5, Assert.IsType<double>(clock.CurrentProgress), Tolerance);
        clock.Controller.Seek(TimeSpan.FromSeconds(0.5), TimeSeekOrigin.Duration);
        Assert.Equal(TimeSpan.FromSeconds(1.5), clock.CurrentTime);
        // Seeking past the end fills and completes.
        var completed = 0;
        clock.Completed += (_, _) => completed++;
        clock.Controller.Seek(TimeSpan.FromSeconds(5), TimeSeekOrigin.BeginTime);
        Assert.Equal(ClockState.Filling, clock.CurrentState);
        Assert.Equal(TimeSpan.FromSeconds(2), clock.CurrentTime);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Clock_skip_to_fill_completes_without_advance()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(4) };
        var clock = Clock.CreateForTests(timeline);
        var completed = 0;
        clock.Completed += (_, _) => completed++;
        clock.Controller.SkipToFill();
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Equal(0, completed);
        clock.Controller.Begin();
        clock.Controller.SkipToFill();
        Assert.Equal(ClockState.Filling, clock.CurrentState);
        Assert.Equal(TimeSpan.FromSeconds(4), clock.CurrentTime);
        Assert.Equal(1.0, clock.CurrentProgress);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Clock_stop_clears_without_completing()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2) };
        var clock = Clock.CreateForTests(timeline);
        var completed = 0;
        clock.Completed += (_, _) => completed++;
        clock.Controller.Begin();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.Controller.Stop();
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Null(clock.CurrentTime);
        Assert.Null(clock.CurrentProgress);
        Assert.Null(clock.CurrentIteration);
        Assert.False(clock.IsPaused);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Clock_remove_raises_remove_requested()
    {
        var clock = Clock.CreateForTests();
        var removed = 0;
        clock.RemoveRequested += (_, _) => removed++;
        clock.Controller.Begin();
        clock.Controller.Remove();
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void Clock_speed_ratio_scales_advance()
    {
        var timeline = new DoubleAnimation { Duration = TimeSpan.FromSeconds(4) };
        var clock = Clock.CreateForTests(timeline);
        clock.Controller.SpeedRatio = 2.0;
        Assert.Equal(2.0, clock.Controller.SpeedRatio);
        clock.Controller.Begin();
        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.CurrentTime);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Controller.SpeedRatio = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Controller.SpeedRatio = -1.0);
        Assert.Equal(2.0, clock.Controller.SpeedRatio);
    }

    [Fact]
    public void Clock_state_values_match_wpf()
    {
        Assert.Equal(0, (int)ClockState.Active);
        Assert.Equal(1, (int)ClockState.Filling);
        Assert.Equal(2, (int)ClockState.Stopped);
        Assert.Equal(0, (int)TimeSeekOrigin.BeginTime);
        Assert.Equal(1, (int)TimeSeekOrigin.Duration);
    }
}
