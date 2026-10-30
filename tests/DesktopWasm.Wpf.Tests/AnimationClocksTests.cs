using DesktopWasm.BrowserHost;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Animation clocks for the ScreenToGif HideableTabControl show/hide paths:
/// PowerEase, AnimationTimeline, ObjectAnimationUsingKeyFrames, KeyTime,
/// DiscreteObjectKeyFrame, ObjectKeyFrame, ThicknessAnimation and
/// UIElement.BeginAnimation, all pumped by the deterministic
/// WpfAnimationClock (no wall time). Mirrors the original call shapes in
/// ScreenToGif/Controls/HideableTabControl.cs and Util/BrushAnimation.cs.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class AnimationClocksTests : IDisposable
{
    public AnimationClocksTests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class BrushProbe : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(Brush);

        protected override Freezable CreateInstanceCore() => new BrushProbe();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
            animationClock.CurrentProgress >= 1.0 ? defaultDestinationValue : defaultOriginValue;

        public Freezable Spawn() => CreateInstanceCore();
    }

    [Fact]
    public void PowerEase_raises_to_power()
    {
        var ease = new PowerEase { Power = 8 };
        Assert.Equal(Math.Pow(0.5, 8), ease.Ease(0.5), precision: 12);
        Assert.Equal(0.0, ease.Ease(0.0), precision: 12);
        Assert.Equal(1.0, ease.Ease(1.0), precision: 12);
    }

    [Fact]
    public void DoubleAnimation_ctor_shape_drives_height_with_easing()
    {
        var border = new Border { Height = 0 };
        var animation = new DoubleAnimation(0, 100, new Duration(TimeSpan.FromSeconds(1)))
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        border.BeginAnimation(FrameworkElement.HeightProperty, animation);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(100 * Math.Pow(0.5, 8), border.Height, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(100.0, border.Height, precision: 9);
    }

    [Fact]
    public void ObjectAnimationUsingKeyFrames_flips_visibility_at_key_time()
    {
        var button = new Button { Visibility = Visibility.Collapsed };
        var show = new ObjectAnimationUsingKeyFrames();
        show.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
        button.BeginAnimation(UIElement.VisibilityProperty, show);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.25));
        Assert.Equal(Visibility.Collapsed, button.Visibility);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.25));
        Assert.Equal(Visibility.Visible, button.Visibility);
    }

    [Fact]
    public void ObjectAnimationUsingKeyFrames_zero_time_clears_selected_item()
    {
        var tabs = new TabControl();
        var first = new TabItem { Header = "A" };
        var second = new TabItem { Header = "B" };
        tabs.AddChild(first);
        tabs.AddChild(second);
        tabs.SelectedItem = first;
        Assert.Equal(0, tabs.SelectedIndex);

        var clear = new ObjectAnimationUsingKeyFrames();
        clear.KeyFrames.Add(new DiscreteObjectKeyFrame(null, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
        tabs.BeginAnimation(Selector.SelectedItemProperty, clear);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Null(tabs.SelectedItem);
        Assert.Equal(-1, tabs.SelectedIndex);
    }

    [Fact]
    public void ThicknessAnimation_interpolates_margin_components()
    {
        var panel = new TabPanel { Margin = new Thickness(5, 5, 0, -1) };
        var animation = new ThicknessAnimation(
            new Thickness(5, 5, 0, -1),
            new Thickness(5, 5, 0, 5),
            new Duration(TimeSpan.FromSeconds(1)))
        {
            EasingFunction = new PowerEase { Power = 8 },
        };
        panel.BeginAnimation(FrameworkElement.MarginProperty, animation);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        var expected = -1 + 6 * Math.Pow(0.5, 8);
        Assert.Equal(expected, panel.Margin.Bottom, precision: 9);
        Assert.Equal(5.0, panel.Margin.Left, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(5.0, panel.Margin.Bottom, precision: 9);
    }

    [Fact]
    public void ColorAnimation_interpolates_through_animation_clock()
    {
        var animation = new ColorAnimation(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255), new Duration(TimeSpan.FromSeconds(2)));
        var mid = animation.GetCurrentValue(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255), TestClock(1.0, 2.0));
        Assert.Equal(Color.FromRgb(128, 128, 128), mid);
        var end = animation.GetCurrentValue(Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255), TestClock(2.0, 2.0));
        Assert.Equal(Color.FromRgb(255, 255, 255), end);
    }

    [Fact]
    public void BrushAnimation_contract_binds_to_engine_timeline()
    {
        var probe = new BrushProbe();
        Assert.Equal(typeof(Brush), probe.TargetPropertyType);
        var held = probe.GetCurrentValue("from", "to", TestClock(0.5, 1.0));
        Assert.Equal("from", held);
        var done = probe.GetCurrentValue("from", "to", TestClock(1.0, 1.0));
        Assert.Equal("to", done);
        Assert.IsType<BrushProbe>(probe.Spawn());
    }

    [Fact]
    public void Opacity_animates_and_fill_stop_restores_base()
    {
        var border = new Border { Opacity = 1.0 };
        var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(1))) { FillBehavior = FillBehavior.Stop };
        border.BeginAnimation(UIElement.OpacityProperty, fade);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(0.5, border.Opacity, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.0));
        Assert.Equal(1.0, border.Opacity, precision: 9);
    }

    [Fact]
    public void BeginAnimation_null_removes_and_compose_snapshots_current()
    {
        var border = new Border { Height = 0 };
        border.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(2)));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.0));
        Assert.Equal(50.0, border.Height, precision: 9);
        border.BeginAnimation(FrameworkElement.HeightProperty, null);
        Assert.Equal(0.0, border.Height, precision: 9);

        border.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(2)));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.0));
        var retarget = new DoubleAnimation(50, 200, TimeSpan.FromSeconds(1));
        border.BeginAnimation(FrameworkElement.HeightProperty, retarget, HandoffBehavior.Compose);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(125.0, border.Height, precision: 9);
    }

    [Fact]
    public void KeyTime_uniform_spreads_and_paced_fails_fast()
    {
        var keys = new ObjectAnimationUsingKeyFrames();
        keys.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Hidden) { KeyTime = KeyTime.Uniform });
        keys.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Collapsed) { KeyTime = KeyTime.Uniform });
        var target = new Button { Visibility = Visibility.Visible };
        target.BeginAnimation(UIElement.VisibilityProperty, keys);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.6));
        Assert.Equal(Visibility.Hidden, target.Visibility);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(Visibility.Collapsed, target.Visibility);

        var paced = new ObjectAnimationUsingKeyFrames();
        paced.KeyFrames.Add(new DiscreteObjectKeyFrame("x") { KeyTime = KeyTime.Paced });
        Assert.Throws<InvalidOperationException>(() =>
            target.BeginAnimation(UIElement.VisibilityProperty, paced));

        var mistyped = new ObjectAnimationUsingKeyFrames();
        mistyped.KeyFrames.Add(new DiscreteObjectKeyFrame("nope", KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.1))));
        Assert.Throws<InvalidOperationException>(() =>
            target.BeginAnimation(UIElement.VisibilityProperty, mistyped));
    }

    private static AnimationClock TestClock(double seconds, double lengthSeconds) =>
        AnimationClock.CreateForTests(TimeSpan.FromSeconds(seconds), seconds / lengthSeconds, TimeSpan.FromSeconds(lengthSeconds));

    [Fact]
    public void Animation_lab_show_hide_runs_real_clocks()
    {
        var window = DemoCatalog.Find("animation").Build();
        var lab = AnimationLab.Current;
        Assert.NotNull(lab);
        Assert.Same(window, lab.Window);
        lab.RefreshReadouts();
        Assert.Equal("Height: 100", lab.Notes["height"]);

        lab.HideButton.RaiseClick();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.99));
        lab.RefreshReadouts();
        var mid = lab.Stage.Height;
        Assert.InRange(mid, 0.0, 100.0);
        Assert.NotEqual(100.0, mid);
        Assert.Equal(Visibility.Collapsed, lab.HideButton.Visibility);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        lab.RefreshReadouts();
        Assert.Equal(0.0, lab.Stage.Height, precision: 9);
        Assert.Equal(0.0, lab.Stage.Opacity, precision: 9);
        Assert.Equal("Height: 0", lab.Notes["height"]);
        Assert.Equal("Visibility: Collapsed", lab.Notes["visibility"]);

        lab.ShowButton.RaiseClick();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.25));
        Assert.Equal(Visibility.Collapsed, lab.HideButton.Visibility);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.25));
        lab.RefreshReadouts();
        Assert.Equal(100.0, lab.Stage.Height, precision: 9);
        Assert.Equal(1.0, lab.Stage.Opacity, precision: 9);
        Assert.Equal(Visibility.Visible, lab.HideButton.Visibility);
        Assert.Equal("Height: 100", lab.Notes["height"]);
        Assert.Equal("Visibility: Visible", lab.Notes["visibility"]);
    }
}
