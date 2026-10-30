namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-72 animation tests: the Int32/Thickness key-frame families,
/// the linear PointAnimation, and the ClockState enum. Sampling runs through
/// the engine pump's test clock; enum values are pinned against the 9.0.20
/// ref pack (ClockState Active=0/Filling=1/Stopped=2).</summary>
[ResetAmbientState]
public sealed class Slice72AnimationTests
{
    private static AnimationClock ClockAt(TimeSpan at, TimeSpan iteration) =>
        AnimationClock.CreateForTests(at, at.Ticks / (double)iteration.Ticks, iteration);

    [Fact]
    public void ClockState_values_match_live_wpf()
    {
        Assert.Equal(0, (int)ClockState.Active);
        Assert.Equal(1, (int)ClockState.Filling);
        Assert.Equal(2, (int)ClockState.Stopped);
    }

    [Fact]
    public void Int32Animation_samples_linear_midpoint()
    {
        var animation = new Int32Animation(0, 100, new Duration(TimeSpan.FromSeconds(2)));
        var clock = ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Equal(50, animation.GetCurrentValue(0, 100, clock));
    }

    [Fact]
    public void Int32Animation_by_resolves_without_to()
    {
        var animation = new Int32Animation { From = 10, By = 5 };
        var duration = TimeSpan.FromSeconds(1);
        var clock = AnimationClock.CreateForTests(duration, 1.0, duration);
        Assert.Equal(15, animation.GetCurrentValue(0, 0, clock));
    }

    [Fact]
    public void Int32Animation_additive_offsets_from_origin()
    {
        var animation = new Int32Animation(0, 100, new Duration(TimeSpan.FromSeconds(2))) { IsAdditive = true };
        var clock = ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Equal(50 + 50, animation.GetCurrentValue(50, 100, clock));
    }

    [Fact]
    public void Int32AnimationUsingKeyFrames_discrete_jumps_at_frame_time()
    {
        var animation = new Int32AnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteInt32KeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));
        animation.KeyFrames.Add(new DiscreteInt32KeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        var iteration = TimeSpan.FromSeconds(3);
        Assert.Equal(0, animation.GetCurrentValue(0, 99, ClockAt(TimeSpan.FromSeconds(0.5), iteration)));
        Assert.Equal(10, animation.GetCurrentValue(0, 99, ClockAt(TimeSpan.FromSeconds(1.5), iteration)));
        Assert.Equal(20, animation.GetCurrentValue(0, 99, ClockAt(TimeSpan.FromSeconds(2.5), iteration)));
        Assert.True(animation.ShouldSerializeKeyFrames());
    }

    [Fact]
    public void Int32AnimationUsingKeyFrames_linear_interpolates_between_frames()
    {
        var animation = new Int32AnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearInt32KeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearInt32KeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        var clock = ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Equal(50, animation.GetCurrentValue(0, 0, clock));
    }

    [Fact]
    public void Int32AnimationUsingKeyFrames_eased_applies_function()
    {
        var animation = new Int32AnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearInt32KeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingInt32KeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), new CubicEase()));
        var clock = ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var eased = new CubicEase().Ease(0.5);
        Assert.Equal((int)(100 * eased), animation.GetCurrentValue(0, 0, clock));
    }

    [Fact]
    public void Int32KeyFrameCollection_add_returns_index_and_clones_deep()
    {
        var frames = new Int32KeyFrameCollection();
        Assert.Equal(0, frames.Add(new LinearInt32KeyFrame(1)));
        Assert.Equal(1, frames.Add(new LinearInt32KeyFrame(2)));
        Assert.Same(Int32KeyFrameCollection.Empty, Int32KeyFrameCollection.Empty);
        var clone = frames.Clone();
        Assert.Equal(2, clone.Count);
        clone[0].Value = 42;
        Assert.Equal(1, frames[0].Value);
        Assert.Throws<ArgumentException>(() => ((System.Collections.IList)frames).Add("nope"));
    }

    [Fact]
    public void Int32KeyFrame_interpolate_defaults_linear()
    {
        var frame = new LinearInt32KeyFrame(100);
        Assert.Equal(50, frame.InterpolateValue(0, 0.5));
        Assert.Equal(0, new DiscreteInt32KeyFrame(100).InterpolateValue(0, 0.5));
        Assert.Equal(100, new DiscreteInt32KeyFrame(100).InterpolateValue(0, 1.0));
    }

    [Fact]
    public void ThicknessAnimationUsingKeyFrames_lerps_sides()
    {
        var animation = new ThicknessAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(10, 20, 30, 40), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        var value = animation.GetCurrentValue(new Thickness(0, 0, 0, 0), new Thickness(0, 0, 0, 0),
            ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Equal(new Thickness(5, 10, 15, 20), value);
    }

    [Fact]
    public void ThicknessKeyFrame_discrete_holds_until_frame_time()
    {
        var frame = new DiscreteThicknessKeyFrame(new Thickness(7, 7, 7, 7));
        Assert.Equal(new Thickness(1, 2, 3, 4), frame.InterpolateValue(new Thickness(1, 2, 3, 4), 0.5));
        Assert.Equal(new Thickness(7, 7, 7, 7), frame.InterpolateValue(new Thickness(1, 2, 3, 4), 1.0));
    }

    [Fact]
    public void ThicknessKeyFrameCollection_add_clone_and_empty()
    {
        var frames = new ThicknessKeyFrameCollection();
        Assert.Equal(0, frames.Add(new LinearThicknessKeyFrame(new Thickness(1, 1, 1, 1))));
        Assert.False(frames.IsFixedSize);
        Assert.False(frames.IsSynchronized);
        var clone = frames.CloneCurrentValue();
        Assert.Equal(1, clone.Count);
        Assert.NotSame(frames[0], clone[0]);
    }

    [Fact]
    public void ThicknessAnimationUsingKeyFrames_empty_holds_origin()
    {
        var animation = new ThicknessAnimationUsingKeyFrames();
        var origin = new Thickness(3, 3, 3, 3);
        Assert.Equal(origin, animation.GetCurrentValue(origin, new Thickness(9, 9, 9, 9),
            ClockAt(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1))));
        Assert.False(animation.ShouldSerializeKeyFrames());
    }

    [Fact]
    public void PointAnimation_samples_linear_midpoint()
    {
        var animation = new PointAnimation(new Point(0, 0), new Point(100, 50), new Duration(TimeSpan.FromSeconds(2)));
        var value = animation.GetCurrentValue(new Point(0, 0), new Point(100, 50),
            ClockAt(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Equal(new Point(50, 25), value);
    }

    [Fact]
    public void PointAnimation_from_defaults_to_origin()
    {
        var animation = new PointAnimation { To = new Point(10, 20) };
        var duration = TimeSpan.FromSeconds(1);
        var value = animation.GetCurrentValue(new Point(2, 4), new Point(10, 20),
            AnimationClock.CreateForTests(duration, 1.0, duration));
        Assert.Equal(new Point(10, 20), value);
    }

    [Fact]
    public void KeyFrame_bases_carry_keytime_and_value_stores()
    {
        var intFrame = new LinearInt32KeyFrame(7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3)));
        Assert.Equal(7, intFrame.Value);
        Assert.Equal(KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3)), intFrame.KeyTime);
        Assert.Equal(7, ((IKeyFrame)intFrame).Value);

        var thickFrame = new LinearThicknessKeyFrame(new Thickness(1, 2, 3, 4));
        Assert.Equal(new Thickness(1, 2, 3, 4), thickFrame.Value);
        Assert.Equal(KeyTime.Uniform, thickFrame.KeyTime);
    }
}
