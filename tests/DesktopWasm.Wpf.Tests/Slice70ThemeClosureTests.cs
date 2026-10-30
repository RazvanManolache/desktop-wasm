using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-70 MaterialDesign theme-closure prerequisites: automation peers
/// (ListBoxItem/Selector/Item/ItemsControl/ListView + PatternInterface),
/// TextSelection over the document model, GridView/GridViewColumn/ViewBase,
/// animation keyframes/collections/groups, NameScope/INameScope,
/// BlurEffect and SkewTransform. Every numeric, default and shape below was
/// verified live against the 9.0.21 WindowsDesktop ref packs plus a live
/// net9.0-windows runtime probe before it was pinned.
/// </summary>
[ResetAmbientState]
public sealed class Slice70ThemeClosureTests
{
    [Fact]
    public void Pattern_interface_numerics_match_wpf()
    {
        Assert.Equal(0, (int)PatternInterface.Invoke);
        Assert.Equal(1, (int)PatternInterface.Selection);
        Assert.Equal(2, (int)PatternInterface.Value);
        Assert.Equal(11, (int)PatternInterface.SelectionItem);
        Assert.Equal(17, (int)PatternInterface.Text);
        Assert.Equal(20, (int)PatternInterface.SynchronizedInput);
    }

    [Fact]
    public void Automation_control_type_numerics_match_wpf()
    {
        Assert.Equal(0, (int)AutomationControlType.Button);
        Assert.Equal(7, (int)AutomationControlType.ListItem);
        Assert.Equal(8, (int)AutomationControlType.List);
        Assert.Equal(23, (int)AutomationControlType.Tree);
        Assert.Equal(24, (int)AutomationControlType.TreeItem);
        Assert.Equal(25, (int)AutomationControlType.Custom);
        Assert.Equal(38, (int)AutomationControlType.Separator);
    }

    [Fact]
    public void Peer_hierarchy_matches_wpf()
    {
        Assert.True(typeof(FrameworkElementAutomationPeer).IsSubclassOf(typeof(UIElementAutomationPeer)));
        Assert.True(typeof(ItemAutomationPeer).IsSubclassOf(typeof(AutomationPeer)));
        Assert.True(typeof(SelectorItemAutomationPeer).IsSubclassOf(typeof(ItemAutomationPeer)));
        Assert.True(typeof(ListBoxItemAutomationPeer).IsSubclassOf(typeof(SelectorItemAutomationPeer)));
        Assert.True(typeof(ItemsControlAutomationPeer).IsSubclassOf(typeof(FrameworkElementAutomationPeer)));
        Assert.True(typeof(SelectorAutomationPeer).IsSubclassOf(typeof(ItemsControlAutomationPeer)));
        Assert.True(typeof(ListBoxAutomationPeer).IsSubclassOf(typeof(SelectorAutomationPeer)));
        Assert.True(typeof(ListViewAutomationPeer).IsSubclassOf(typeof(ListBoxAutomationPeer)));
    }

    [Fact]
    public void Item_peer_carries_item_and_parent()
    {
        var owner = new ListView();
        var parent = new ListViewAutomationPeer(owner);
        var peer = new ListBoxItemAutomationPeer("row", new SelectorAutomationPeer(new ComboBox()));
        Assert.Equal("row", peer.Item);
        Assert.NotNull(peer.ItemsControlAutomationPeer);
        Assert.Same(owner, parent.Owner);
        Assert.Null(parent.GetPattern(PatternInterface.SelectionItem));

        var itemPeer = new ItemAutomationPeer(42, parent);
        Assert.Equal(42, itemPeer.Item);
        Assert.Same(parent, itemPeer.ItemsControlAutomationPeer);
        Assert.Throws<ArgumentNullException>(() => new ItemAutomationPeer(null!, parent));
        Assert.Throws<ArgumentNullException>(() => new ItemAutomationPeer(42, null!));
    }

    [Fact]
    public void Converted_peer_override_slots_bind()
    {
        var owner = new ListView();
        var peer = new TreePeer(owner);
        Assert.Equal(AutomationControlType.Tree, peer.ExposedControlType());
        Assert.Equal("pattern", peer.GetPattern(PatternInterface.ItemContainer));
        Assert.Null(peer.GetPattern(PatternInterface.Invoke));
        var children = peer.ExposedChildren();
        Assert.NotNull(children);
        Assert.Empty(children);
    }

    private sealed class TreePeer : ListViewAutomationPeer
    {
        public TreePeer(ListView owner) : base(owner)
        {
        }

        public AutomationControlType ExposedControlType() => GetAutomationControlTypeCore();

        public List<AutomationPeer>? ExposedChildren() => GetChildrenCore();

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ItemContainer ? "pattern" : null;

        protected override List<AutomationPeer>? GetChildrenCore() => new();
    }

    [Fact]
    public void Animatable_is_the_real_animation_host()
    {
        Assert.True(typeof(Brush).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(Timeline).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(Geometry).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(GeneralTransform).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(Effect).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(Animatable).IsSubclassOf(typeof(Freezable)));
        var brush = new SolidColorBrush(Colors.Red);
        var clone = ((Animatable)brush).Clone();
        Assert.IsType<SolidColorBrush>(clone);
    }

    [Fact]
    public void Storyboard_chain_matches_wpf()
    {
        Assert.True(typeof(TimelineGroup).IsSubclassOf(typeof(Timeline)));
        Assert.True(typeof(ParallelTimeline).IsSubclassOf(typeof(TimelineGroup)));
        Assert.True(typeof(Storyboard).IsSubclassOf(typeof(ParallelTimeline)));
        var board = new Storyboard();
        Assert.Empty(board.Children);
        Assert.Same(board.Children, ((TimelineGroup)board).Children);
        var parallel = new ParallelTimeline(TimeSpan.FromSeconds(1), Duration.Automatic);
        Assert.Equal(TimeSpan.FromSeconds(1), parallel.BeginTime);
        Assert.Equal(SlipBehavior.Grow, parallel.SlipBehavior);
        Assert.Equal(0, (int)SlipBehavior.Grow);
        Assert.Equal(1, (int)SlipBehavior.Slip);
    }

    [Fact]
    public void Keyframe_shapes_match_wpf()
    {
        Assert.True(typeof(DoubleKeyFrame).IsSubclassOf(typeof(Freezable)));
        Assert.True(typeof(DiscreteDoubleKeyFrame).IsSubclassOf(typeof(DoubleKeyFrame)));
        Assert.True(typeof(LinearDoubleKeyFrame).IsSubclassOf(typeof(DoubleKeyFrame)));
        Assert.True(typeof(EasingDoubleKeyFrame).IsSubclassOf(typeof(DoubleKeyFrame)));
        Assert.True(typeof(DoubleAnimationUsingKeyFrames).IsSubclassOf(typeof(AnimationTimeline)));
        Assert.True(typeof(SineEase).IsSubclassOf(typeof(EasingFunctionBase)));
        Assert.True(typeof(CubicEase).IsSubclassOf(typeof(EasingFunctionBase)));

        var frame = new DiscreteDoubleKeyFrame();
        Assert.Equal(0.0, frame.Value);
        Assert.Equal(KeyTime.Uniform, frame.KeyTime);
        var timed = new LinearDoubleKeyFrame(3.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)));
        Assert.Equal(3.0, timed.Value);
        Assert.Equal(TimeSpan.FromSeconds(2), timed.KeyTime.Time);
        var eased = new EasingDoubleKeyFrame(4.0, KeyTime.FromPercent(50), new SineEase());
        Assert.Equal(4.0, eased.Value);
        Assert.NotNull(eased.EasingFunction);
        Assert.Same(DoubleKeyFrame.KeyTimeProperty, DependencyProperty.FromName(typeof(DoubleKeyFrame), "KeyTime"));
        Assert.Same(DoubleKeyFrame.ValueProperty, DependencyProperty.FromName(typeof(DoubleKeyFrame), "Value"));

        var keyed = new DoubleAnimationUsingKeyFrames();
        Assert.Empty(keyed.KeyFrames);
        Assert.Equal(typeof(double), keyed.TargetPropertyType);
        Assert.False(keyed.IsAdditive);
        Assert.False(keyed.IsCumulative);
    }

    [Fact]
    public void Sine_and_cubic_formulas_match_live_wpf()
    {
        // Live net9.0-windows probe: EaseIn(0.5) is 1-cos(pi/4) for sine, 1/8 for cubic.
        var sine = new SineEase { EasingMode = EasingMode.EaseIn };
        Assert.Equal(1.0 - Math.Cos(Math.PI / 4.0), sine.Ease(0.5), precision: 12);
        var cubic = new CubicEase { EasingMode = EasingMode.EaseIn };
        Assert.Equal(0.125, cubic.Ease(0.5), precision: 12);
    }

    [Fact]
    public void Keyed_double_animation_interpolates_segments()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(10.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10))));
        var duration = TimeSpan.FromSeconds(10);
        Assert.Equal(0.0, Sample(animation, origin: 99.0, at: TimeSpan.Zero, duration));
        Assert.Equal(5.0, Sample(animation, 99.0, TimeSpan.FromSeconds(5), duration), precision: 9);
        Assert.Equal(10.0, Sample(animation, 99.0, TimeSpan.FromSeconds(10), duration), precision: 9);
        // Past the end holds the last frame; empty holds the origin.
        Assert.Equal(10.0, Sample(animation, 99.0, TimeSpan.FromSeconds(30), duration), precision: 9);
        Assert.Equal(99.0, Sample(new DoubleAnimationUsingKeyFrames(), 99.0, TimeSpan.FromSeconds(5), duration));
    }

    [Fact]
    public void Keyed_double_animation_honors_discrete_and_easing()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(2.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(8.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10))));
        var duration = TimeSpan.FromSeconds(10);
        Assert.Equal(2.0, Sample(animation, 0.0, TimeSpan.FromSeconds(5), duration));
        Assert.Equal(8.0, Sample(animation, 0.0, TimeSpan.FromSeconds(10), duration));

        var eased = new DoubleAnimationUsingKeyFrames();
        eased.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        eased.KeyFrames.Add(new EasingDoubleKeyFrame(10.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10)), new CubicEase { EasingMode = EasingMode.EaseIn }));
        Assert.Equal(1.25, Sample(eased, 0.0, TimeSpan.FromSeconds(5), duration), precision: 9);
    }

    private static double Sample(DoubleAnimationUsingKeyFrames animation, double origin, TimeSpan at, TimeSpan duration)
    {
        var clock = AnimationClock.CreateForTests(at, 0.5, duration);
        return animation.GetCurrentValue(origin, double.NaN, clock);
    }

    [Fact]
    public void Grid_view_shapes_match_wpf()
    {
        Assert.True(typeof(GridView).IsSubclassOf(typeof(ViewBase)));
        Assert.True(typeof(ViewBase).IsSubclassOf(typeof(DependencyObject)));
        Assert.True(typeof(GridViewColumnCollection).IsSubclassOf(typeof(ObservableCollection<GridViewColumn>)));

        var view = new GridView();
        Assert.Empty(view.Columns);
        Assert.True(view.AllowsColumnReorder);

        var column = new GridViewColumn();
        Assert.True(double.IsNaN(column.Width));
        Assert.Equal(0.0, column.ActualWidth);
        Assert.Null(column.Header);
        Assert.Null(column.CellTemplate);
        Assert.Same(GridViewColumn.WidthProperty, DependencyProperty.FromName(typeof(GridViewColumn), "Width"));
        column.Width = 120;
        Assert.Equal(120.0, column.Width);
        column.Header = "Name";
        Assert.Equal("Name", column.Header);
        view.Columns.Add(column);
        Assert.Same(column, view.Columns[0]);

        var list = new ListView();
        Assert.Null(list.View);
        list.View = view;
        Assert.Same(view, list.View);
        Assert.Same(ListView.ViewProperty, DependencyProperty.FromName(typeof(ListView), "View"));
    }

    [Fact]
    public void Blur_effect_defaults_match_live_wpf()
    {
        var blur = new BlurEffect();
        Assert.Equal(5.0, blur.Radius);
        Assert.Equal(KernelType.Gaussian, blur.KernelType);
        Assert.Equal(RenderingBias.Performance, blur.RenderingBias);
        Assert.Equal(0, (int)KernelType.Gaussian);
        Assert.Equal(1, (int)KernelType.Box);
        Assert.Same(BlurEffect.RadiusProperty, DependencyProperty.FromName(typeof(BlurEffect), "Radius"));
        blur.Radius = 12;
        Assert.Equal(12.0, blur.Radius);
        Assert.True(blur is Animatable);
    }

    [Fact]
    public void Skew_transform_matrix_matches_live_wpf()
    {
        // Live net9.0-windows probe for SkewTransform(30, 20, 5, 7).
        var skew = new SkewTransform(30, 20, 5, 7);
        var value = skew.Value;
        Assert.Equal(1.0, value.M11, precision: 12);
        Assert.Equal(Math.Tan(20 * Math.PI / 180.0), value.M12, precision: 12);
        Assert.Equal(Math.Tan(30 * Math.PI / 180.0), value.M21, precision: 12);
        Assert.Equal(1.0, value.M22, precision: 12);
        Assert.Equal(-4.04145188432738, value.OffsetX, precision: 9);
        Assert.Equal(-1.8198511713310115, value.OffsetY, precision: 9);

        var plain = new SkewTransform();
        Assert.Equal(0.0, plain.AngleX);
        Assert.Equal(new Matrix(1, 0, 0, 1, 0, 0), plain.Value);
        Assert.True(plain.Transform(new Point(3, 4)).X > 0);
    }

    [Fact]
    public void Text_range_slices_live_document_text()
    {
        var paragraph = new Paragraph(new Run("Hello"));
        paragraph.Inlines.Add(new Run(" world"));
        var document = new FlowDocument(paragraph);
        var start = document.ContentStart;
        var end = document.ContentEnd;
        Assert.Equal(0, start.Offset);
        Assert.Equal(document.Text.Length, end.Offset);
        var range = new TextRange(start, end);
        Assert.Equal(document.Text, range.Text);
        Assert.False(range.IsEmpty);

        var selection = new TextSelection(start, start.GetPositionAtOffset(5));
        Assert.Equal("Hello", selection.Text);
        Assert.False(selection.IsEmpty);
        selection.Select(start, start);
        Assert.True(selection.IsEmpty);
        Assert.Equal(string.Empty, selection.Text);
        Assert.Throws<ArgumentNullException>(() => new TextRange(null!, end));
        var other = new FlowDocument(new Paragraph(new Run("other")));
        Assert.Throws<ArgumentException>(() => new TextRange(start, other.ContentStart));
        Assert.Equal(start, new TextPointer(document, -3));
        Assert.Equal(end, start.GetPositionAtOffset(1 << 20));
        Assert.Equal(5, start.GetOffsetToPosition(start.GetPositionAtOffset(5)));
    }

    [Fact]
    public void Name_scope_registers_and_attaches()
    {
        var scope = new NameScope();
        var target = new object();
        Assert.Null(scope.FindName("part"));
        scope.RegisterName("part", target);
        Assert.Same(target, scope.FindName("part"));
        Assert.Throws<ArgumentException>(() => scope.RegisterName("part", new object()));
        scope.UnregisterName("part");
        Assert.Null(scope.FindName("part"));
        scope.UnregisterName("missing-keeps-quiet");
        Assert.Throws<ArgumentNullException>(() => scope.RegisterName(null!, target));

        var element = new Button();
        Assert.Null(NameScope.GetNameScope(element));
        NameScope.SetNameScope(element, scope);
        Assert.Same(scope, NameScope.GetNameScope(element));
        NameScope.SetNameScope(element, null);
        Assert.Null(NameScope.GetNameScope(element));
        Assert.Throws<ArgumentNullException>(() => NameScope.GetNameScope(null!));

        scope["key"] = target;
        Assert.Same(target, scope["key"]);
        Assert.True(scope.ContainsKey("key"));
        Assert.Equal(2 - 1, scope.Count);
    }
}
