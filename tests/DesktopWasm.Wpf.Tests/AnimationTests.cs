namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Deterministic animation clock (Storyboard/Timeline/DoubleAnimation):
/// interpolation math, repetition, lifecycle and XAML resource loading.
/// The clock only moves through WpfAnimationClock.Advance, so no test
/// depends on wall time.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class AnimationTests : IDisposable
{
    public AnimationTests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class Dial : FrameworkElement
    {
        public double Angle { get; set; }

        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }
    }

    private static (Storyboard Board, DoubleAnimation Animation, Dial Target) Setup(
        double durationSeconds = 1.0,
        double? from = 0.0,
        double? to = 360.0,
        double? by = null)
    {
        var target = new Dial { Name = "rotate" };
        var animation = new DoubleAnimation
        {
            Duration = TimeSpan.FromSeconds(durationSeconds),
            From = from,
            To = to,
            By = by,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty((object)animation, "Angle");
        var board = new Storyboard();
        board.Children.Add(animation);
        return (board, animation, target);
    }

    [Fact]
    public void Linear_interpolation_hits_midpoint_and_end()
    {
        var (board, _, target) = Setup();
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(180.0, target.Angle, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(360.0, target.Angle, precision: 9);
    }

    [Fact]
    public void From_defaults_to_current_and_By_is_relative()
    {
        var target = new Dial { Angle = 10.0 };
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(2), By = 20.0 };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty((object)animation, "Angle");
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(30.0, target.Angle, precision: 9);
    }

    [Fact]
    public void BeginTime_delays_and_RepeatBehavior_counts()
    {
        var (board, animation, target) = Setup();
        animation.BeginTime = TimeSpan.FromSeconds(1);
        animation.RepeatBehavior = new RepeatBehavior(2);
        var completed = 0;
        board.Completed += (_, _) => completed++;
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(180.0, target.Angle, precision: 9);
        Assert.Equal(0, completed);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(360.0, target.Angle, precision: 9);
        Assert.Equal(1, completed);
        // Settled values stay put on further pumps.
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(360.0, target.Angle, precision: 9);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void AutoReverse_ping_pongs_and_forever_never_completes()
    {
        var (board, animation, target) = Setup();
        animation.AutoReverse = true;
        animation.RepeatBehavior = RepeatBehavior.Forever;
        var completed = 0;
        board.Completed += (_, _) => completed++;
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(180.0, target.Angle, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1.0));
        Assert.Equal(180.0, target.Angle, precision: 9);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Stop_freezes_and_Begin_restarts_from_zero()
    {
        var (board, _, target) = Setup();
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.25));
        Assert.Equal(90.0, target.Angle, precision: 9);
        board.Stop();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(90.0, target.Angle, precision: 9);
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(180.0, target.Angle, precision: 9);
    }

    [Fact]
    public void TargetName_resolves_under_scope_and_missing_names_fail()
    {
        var holder = new StackPanel();
        var dial = new Dial { Name = "xaxis" };
        holder.AddChild(dial);
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), To = 45.0 };
        Storyboard.SetTargetName(animation, "xaxis");
        Storyboard.SetTargetProperty((object)animation, "Angle");
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin(holder);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(45.0, dial.Angle, precision: 9);

        var orphan = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), To = 1.0 };
        Storyboard.SetTargetName(orphan, "missing");
        Storyboard.SetTargetProperty((object)orphan, "Angle");
        var bad = new Storyboard();
        bad.Children.Add(orphan);
        Assert.Throws<InvalidOperationException>(() => bad.Begin(holder));
    }

    [Fact]
    public void Bad_paths_and_non_double_targets_fail_precisely()
    {
        var dial = new Dial { Name = "d" };
        var dotted = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), To = 1.0 };
        Storyboard.SetTarget(dotted, dial);
        Storyboard.SetTargetProperty((object)dotted, "RenderTransform.Angle");
        var board = new Storyboard();
        board.Children.Add(dotted);
        Assert.Throws<InvalidOperationException>(() => board.Begin());
        WpfAnimationClock.ResetForTests();

        var text = new TextBlock { Text = "x" };
        var wrong = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), To = 1.0 };
        Storyboard.SetTarget(wrong, text);
        Storyboard.SetTargetProperty((object)wrong, "Text");
        var board2 = new Storyboard();
        board2.Children.Add(wrong);
        Assert.Throws<InvalidOperationException>(() => board2.Begin());
    }

    [Fact]
    public void Indexed_target_resolves_clr_set_RenderTransform()
    {
        // MaterialDesign linear ProgressBar shape: the template sets the
        // part's RenderTransform through the CLR slot while the DP store
        // keeps its default (Slice162 contract), so the indexed walk reads
        // the CLR value when the DP is unwritten (boot forensics: OnLoaded
        // targets (TransformGroup.Children)[0].ScaleX on TemplateRoot).
        var root = new Grid { Name = "TemplateRoot" };
        var group = new TransformGroup();
        var scale = new ScaleTransform { ScaleX = 0 };
        group.Children.Add(scale);
        root.RenderTransform = group;
        var animation = new DoubleAnimation { Duration = TimeSpan.FromSeconds(1), From = 0.0, To = 1.0 };
        Storyboard.SetTarget(animation, root);
        Storyboard.SetTargetProperty((object)animation, "(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)");
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin();
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1.0, scale.ScaleX, precision: 9);
    }

    [Fact]
    public void Non_double_timelines_fail_fast()
    {
        var board = new Storyboard();
        board.Children.Add(new CustomTimeline());
        Assert.Throws<InvalidOperationException>(() => board.Begin());
    }

    private sealed class CustomTimeline : Timeline
    {
    }

    [Fact]
    public void Xaml_resource_storyboard_parses_with_targets()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var result = XamlLoader.Load(
            $"<StackPanel {ns}><StackPanel.Resources><Storyboard x:Key=\"Spin\">" +
            "<DoubleAnimation Storyboard.TargetName=\"rotate\" Storyboard.TargetProperty=\"Angle\" From=\"0\" To=\"360\" Duration=\"0:0:0.5\" RepeatBehavior=\"Forever\" />" +
            "</Storyboard></StackPanel.Resources><TextBlock>Hello</TextBlock></StackPanel>");
        var panel = Assert.IsType<StackPanel>(result.Root);
        Assert.True(panel.Resources.TryGetValue("Spin", out var value));
        var board = Assert.IsType<Storyboard>(value);
        var child = Assert.IsType<DoubleAnimation>(Assert.Single(board.Children));
        Assert.Equal(TimeSpan.FromSeconds(0.5), child.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, child.RepeatBehavior);
        Assert.Equal("rotate", Storyboard.GetTargetName(child));
        Assert.Equal("Angle", Storyboard.GetTargetProperty(child));
        Assert.Equal(0.0, child.From);
        Assert.Equal(360.0, child.To);
    }

    [Fact]
    public void Xaml_timeline_values_reject_garbage_precisely()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load($"<StackPanel {ns}><StackPanel.Resources><Storyboard x:Key=\"S\"><DoubleAnimation To=\"nope\" Duration=\"0:0:1\" /></Storyboard></StackPanel.Resources></StackPanel>"));
        Assert.Contains("not a number", ex.Message);
        ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load($"<StackPanel {ns}><StackPanel.Resources><Storyboard x:Key=\"S\" Duration=\"whenever\" /></StackPanel.Resources></StackPanel>"));
        Assert.Contains("Duration", ex.Message);
    }

    [Fact]
    public void Duration_and_repeat_parse_round_trip()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), Duration.Parse("0:1:30").TimeSpan);
        Assert.Equal(Duration.Forever, Duration.Parse("Forever"));
        Assert.Equal(Duration.Automatic, Duration.Parse("Automatic"));
        Assert.Equal(new RepeatBehavior(3), RepeatBehavior.Parse("3x"));
        Assert.Equal(RepeatBehavior.Forever, RepeatBehavior.Parse("Forever"));
        Assert.Equal(TimeSpan.FromSeconds(5), RepeatBehavior.Parse("0:0:5").RepeatDuration);
    }
}
