namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-70 clock-driven proofs: Animatable.BeginAnimation evaluates the
/// double key-frame animation through the deterministic pump, and the new
/// Storyboard Seek/Resume/Completed contract behaves. Uses the shared
/// WpfAnimationClock test pump (no wall time).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice70KeyFramePumpTests : IDisposable
{
    public Slice70KeyFramePumpTests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class AnimatedWidget : Animatable
    {
        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register("Level", typeof(double), typeof(AnimatedWidget), 0.0);

        public double Level
        {
            get => (double)GetValue(LevelProperty)!;
            set => SetValue(LevelProperty, value);
        }
    }

    private sealed class Dial : FrameworkElement
    {
        public double Angle { get; set; }
    }

    [Fact]
    public void Animatable_begin_animation_pumps_key_frames()
    {
        var widget = new AnimatedWidget();
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(10.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10))));
        widget.BeginAnimation(AnimatedWidget.LevelProperty, animation);
        Assert.Equal(0.0, widget.Level);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(5.0, widget.Level, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(10.0, widget.Level, precision: 9);
        widget.BeginAnimation(AnimatedWidget.LevelProperty, null);
    }

    [Fact]
    public void Animatable_begin_animation_rejects_paced_and_mismatches()
    {
        var widget = new AnimatedWidget();
        var paced = new DoubleAnimationUsingKeyFrames();
        paced.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.Paced));
        Assert.Throws<InvalidOperationException>(() => widget.BeginAnimation(AnimatedWidget.LevelProperty, paced));

        var text = new TextBox();
        var keyed = new DoubleAnimationUsingKeyFrames();
        keyed.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        Assert.Throws<InvalidOperationException>(() =>
            WpfAnimationClock.BeginAnimation(text, UIElement.VisibilityProperty, keyed, HandoffBehavior.SnapshotAndReplace));
    }

    [Fact]
    public void Storyboard_seek_resume_and_completed()
    {
        var dial = new Dial();
        var animation = new DoubleAnimation(0.0, 100.0, new Duration(TimeSpan.FromSeconds(10)));
        var board = new Storyboard();
        board.Children.Add(animation);
        Storyboard.SetTarget(animation, dial);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Angle"));
        Assert.Equal("Angle", Storyboard.GetTargetProperty(animation));
        Assert.Same(dial, Storyboard.GetTarget(animation));

        var completed = 0;
        board.Completed += (_, _) => completed++;
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(40.0, dial.Angle, precision: 9);
        board.Seek(TimeSpan.FromSeconds(8));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(90.0, dial.Angle, precision: 9);
        board.Resume();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(100.0, dial.Angle, precision: 9);
        Assert.Equal(1, completed);

        Assert.Throws<InvalidOperationException>(() => new Storyboard().Seek(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => new Storyboard().Resume());
    }

    [Fact]
    public void Storyboard_thickness_keyframes_animate_margin()
    {
        var grid = new Grid();
        var animation = new ThicknessAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 10), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10))));
        var board = new Storyboard();
        board.Children.Add(animation);
        Storyboard.SetTarget(animation, grid);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Margin"));
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new Thickness(0, 0, 0, 5), grid.Margin);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new Thickness(0, 0, 0, 10), grid.Margin);
    }

    [Fact]
    public void Storyboard_thickness_animation_lerps_margin()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 20, 0) };
        var animation = new ThicknessAnimation
        {
            To = new Thickness(0, 0, 0, 0),
            Duration = new Duration(TimeSpan.FromSeconds(10)),
        };
        var board = new Storyboard();
        board.Children.Add(animation);
        Storyboard.SetTarget(animation, grid);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Margin"));
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new Thickness(0, 0, 10, 0), grid.Margin);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new Thickness(0, 0, 0, 0), grid.Margin);
    }

    [Fact]
    public void Storyboard_dependency_object_overloads_share_the_object_store()
    {
        var dial = new Dial();
        var animation = new DoubleAnimation();
        DependencyObject element = animation;
        Storyboard.SetTargetName(element, "dial");
        Assert.Equal("dial", Storyboard.GetTargetName((object)animation));
        Storyboard.SetTarget(element, dial);
        Assert.Same(dial, Storyboard.GetTarget((object)animation));
        Storyboard.SetTargetProperty(element, new PropertyPath("Angle"));
        Assert.Equal("Angle", Storyboard.GetTargetProperty((object)animation));
    }
}
