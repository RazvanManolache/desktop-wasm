namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-71 visual-state cluster: VisualStateManager/VisualState/VisualStateGroup/
/// VisualTransition/VisualStateChangedEventArgs/TemplateVisualStateAttribute plus
/// the FreezableCollection&lt;T&gt; concrete list States/Transitions return. Every
/// default, hierarchy and transition rule below was verified against the
/// dotnet/wpf source plus a live net9.0-windows runtime probe (groups default
/// to an empty per-element list, GoToState reads the template root so it is
/// false without one, unknown states stay false with no events, same-state
/// stays true with no events, VisualStateGroupsProperty is read-only,
/// TemplateVisualStateAttribute is Class/AllowMultiple).
/// Uses the shared WpfAnimationClock test pump (no wall time).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice71VisualStateTests : IDisposable
{
    public Slice71VisualStateTests() => WpfAnimationClock.ResetForTests();

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class Dial : FrameworkElement
    {
        public double Angle { get; set; }
    }

    private sealed class RecordingManager : VisualStateManager
    {
        public readonly List<string> Calls = new();
        public bool Result;

        protected override bool GoToStateCore(FrameworkElement? control, FrameworkElement? stateGroupsRoot, string stateName, VisualStateGroup? group, VisualState? state, bool useTransitions)
        {
            Calls.Add($"{stateName}:{(group is null ? "nogroup" : group.Name)}:{(state is null ? "nostate" : state.Name)}");
            return Result;
        }
    }

    private static (Border Root, VisualStateGroup Group, VisualState Off, VisualState On) ArrangeGroup()
    {
        var root = new Border();
        var group = new VisualStateGroup { Name = "Common" };
        var off = new VisualState { Name = "Off" };
        var on = new VisualState { Name = "On" };
        group.States.Add(off);
        group.States.Add(on);
        VisualStateManager.GetVisualStateGroups(root).Add(group);
        return (root, group, off, on);
    }

    private static Storyboard SpinBoard(Dial dial, double to, double seconds)
    {
        var animation = new DoubleAnimation(0.0, to, new Duration(TimeSpan.FromSeconds(seconds)));
        var board = new Storyboard();
        board.Children.Add(animation);
        Storyboard.SetTarget(animation, dial);
        Storyboard.SetTargetProperty((object)animation, "Angle");
        return board;
    }

    [Fact]
    public void Hierarchy_matches_wpf()
    {
        Assert.True(typeof(VisualStateManager).IsSubclassOf(typeof(DependencyObject)));
        Assert.True(typeof(VisualState).IsSubclassOf(typeof(DependencyObject)));
        Assert.True(typeof(VisualStateGroup).IsSubclassOf(typeof(DependencyObject)));
        Assert.True(typeof(VisualTransition).IsSubclassOf(typeof(DependencyObject)));
        Assert.True(typeof(FreezableCollection<VisualState>).IsSubclassOf(typeof(Animatable)));
        Assert.True(typeof(VisualStateChangedEventArgs).IsSubclassOf(typeof(EventArgs)));
        Assert.True(typeof(TemplateVisualStateAttribute).IsSubclassOf(typeof(Attribute)));
        Assert.True(typeof(TemplateVisualStateAttribute).IsSealed);
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(TemplateVisualStateAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        var constraint = typeof(FreezableCollection<>).GetGenericArguments()[0];
        Assert.Contains(typeof(DependencyObject), constraint.GetGenericParameterConstraints());
    }

    [Fact]
    public void Defaults_match_live_probe()
    {
        var state = new VisualState();
        Assert.Null(state.Name);
        Assert.Null(state.Storyboard);

        var transition = new VisualTransition();
        Assert.Null(transition.From);
        Assert.Null(transition.To);
        Assert.Null(transition.Storyboard);
        Assert.Null(transition.GeneratedEasingFunction);
        Assert.Equal(new Duration(TimeSpan.Zero), transition.GeneratedDuration);

        var group = new VisualStateGroup();
        Assert.Null(group.Name);
        Assert.Null(group.CurrentState);
        Assert.IsType<FreezableCollection<VisualState>>(group.States);
        Assert.IsType<FreezableCollection<VisualTransition>>(group.Transitions);
        Assert.Empty(group.States);
        Assert.Empty(group.Transitions);

        var first = new Border();
        var groups = VisualStateManager.GetVisualStateGroups(first);
        Assert.NotNull(groups);
        Assert.Empty(groups);
        Assert.Same(groups, VisualStateManager.GetVisualStateGroups(first));
        Assert.NotSame(groups, VisualStateManager.GetVisualStateGroups(new Border()));
        Assert.Null(VisualStateManager.GetCustomVisualStateManager(first));
    }

    [Fact]
    public void Template_visual_state_attribute_carries_names()
    {
        var attribute = new TemplateVisualStateAttribute { Name = "Pressed", GroupName = "Common" };
        Assert.Equal("Pressed", attribute.Name);
        Assert.Equal("Common", attribute.GroupName);
    }

    [Fact]
    public void Visual_state_groups_property_is_read_only()
    {
        var root = new Border();
        Assert.Throws<InvalidOperationException>(() =>
            root.SetValue(VisualStateManager.VisualStateGroupsProperty, new object()));
        var manager = new Border();
        var custom = new VisualStateManager();
        VisualStateManager.SetCustomVisualStateManager(manager, custom);
        Assert.Same(custom, VisualStateManager.GetCustomVisualStateManager(manager));
        VisualStateManager.SetCustomVisualStateManager(manager, null);
        Assert.Null(VisualStateManager.GetCustomVisualStateManager(manager));
    }

    [Fact]
    public void Freezable_collection_behaves_like_a_list()
    {
        var states = new FreezableCollection<VisualState>(capacity: 4);
        Assert.Empty(states);
        var off = new VisualState { Name = "Off" };
        var on = new VisualState { Name = "On" };
        states.Add(off);
        states.Add(on);
        Assert.Equal(2, states.Count);
        Assert.Same(off, states[0]);
        Assert.Contains(on, states);
        Assert.Equal(1, states.IndexOf(on));
        states.Insert(1, new VisualState { Name = "Mid" });
        Assert.Equal("Mid", states[1].Name);
        Assert.True(states.Remove(off));
        Assert.False(states.Remove(off));
        states.RemoveAt(0);
        Assert.Single(states);
        states.Clear();
        Assert.Empty(states);

        System.Collections.IList facade = states;
        Assert.Equal(0, facade.Add(on));
        Assert.Equal(0, facade.IndexOf(on));
        Assert.Throws<ArgumentException>(() => { facade.Add("nope"); });
        Assert.False(facade.Contains("nope"));
        Assert.Equal(-1, facade.IndexOf("nope"));
        Assert.False(facade.IsFixedSize);
        Assert.False(facade.IsReadOnly);
        Assert.False(((System.Collections.ICollection)facade).IsSynchronized);

        var seeded = new FreezableCollection<VisualState>(new[] { on });
        Assert.Single(seeded);
        var names = new List<string>();
        foreach (var item in seeded) names.Add(item.Name!);
        Assert.Equal(["On"], names);
    }

    [Fact]
    public void Freezable_collection_raises_change_notices()
    {
        var states = new FreezableCollection<VisualState>();
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        var props = new List<string?>();
        states.CollectionChanged += (_, e) => changes.Add(e);
        ((System.ComponentModel.INotifyPropertyChanged)states).PropertyChanged += (_, e) => props.Add(e.PropertyName);
        var on = new VisualState { Name = "On" };
        states.Add(on);
        Assert.Single(changes);
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Add, changes[0].Action);
        Assert.Same(on, changes[0].NewItems![0]);
        Assert.Equal(0, changes[0].NewStartingIndex);
        Assert.Contains("Count", props);
        Assert.Contains("Item[]", props);

        states[0] = new VisualState { Name = "Off" };
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Replace, changes[1].Action);
        states.Clear();
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes[2].Action);
    }

    [Fact]
    public void Freezable_collection_freeze_blocks_writes_and_clone_splits_the_list()
    {
        var states = new FreezableCollection<VisualState> { new VisualState { Name = "On" } };
        Assert.False(((System.Collections.Generic.ICollection<VisualState>)states).IsReadOnly);
        states.Freeze();
        Assert.True(states.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => states.Add(new VisualState()));
        Assert.Throws<InvalidOperationException>(() => states.Clear());

        var live = new FreezableCollection<VisualState> { new VisualState { Name = "On" } };
        var clone = live.Clone();
        Assert.IsType<FreezableCollection<VisualState>>(clone);
        Assert.Single(clone);
        clone.Add(new VisualState { Name = "Off" });
        Assert.Equal(2, clone.Count);
        Assert.Single(live);
        Assert.Same(live[0], clone[0]);
        Assert.False(clone.IsFrozen);
    }

    [Fact]
    public void Go_to_state_validates_arguments()
    {
        var root = new Border();
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToState(null!, "On", false));
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToState(root, null!, false));
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToElementState(null!, "On", false));
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToElementState(root, null!, false));
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GetVisualStateGroups(null!));
    }

    [Fact]
    public void Go_to_state_without_a_template_root_is_false()
    {
        // WPF parity: GoToState reads control.StateGroupsRoot (the applied
        // template root), not the control itself, so an untemplated control
        // never transitions even with directly attached groups.
        var control = new Button();
        VisualStateManager.GetVisualStateGroups(control).Add(new VisualStateGroup { Name = "G" });
        Assert.False(VisualStateManager.GoToState(control, "G", false));

        var root = new Border();
        var group = new VisualStateGroup { Name = "G" };
        group.States.Add(new VisualState { Name = "On" });
        VisualStateManager.GetVisualStateGroups(root).Add(group);
        control.TemplateRoot = root;
        Assert.True(VisualStateManager.GoToState(control, "On", false));
        Assert.Equal("On", group.CurrentState?.Name);
    }

    [Fact]
    public void Go_to_unknown_state_is_false_with_no_events()
    {
        var (root, group, _, _) = ArrangeGroup();
        var changing = 0;
        var changed = 0;
        group.CurrentStateChanging += (_, _) => changing++;
        group.CurrentStateChanged += (_, _) => changed++;
        Assert.False(VisualStateManager.GoToElementState(root, "Missing", false));
        Assert.Null(group.CurrentState);
        Assert.Equal(0, changing);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Go_to_same_state_is_true_with_no_events()
    {
        var (root, group, _, on) = ArrangeGroup();
        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        Assert.Same(on, group.CurrentState);
        var changing = 0;
        var changed = 0;
        group.CurrentStateChanging += (_, _) => changing++;
        group.CurrentStateChanged += (_, _) => changed++;
        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        Assert.Equal(0, changing);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Go_to_state_runs_the_storyboard_and_fires_in_order()
    {
        var dial = new Dial();
        var (root, group, off, on) = ArrangeGroup();
        on.Storyboard = SpinBoard(dial, 100.0, 10.0);
        var order = new List<string>();
        VisualStateChangedEventArgs? changingArgs = null;
        VisualStateChangedEventArgs? changedArgs = null;
        group.CurrentStateChanging += (_, e) => { order.Add("changing"); changingArgs = e; };
        group.CurrentStateChanged += (_, e) => { order.Add("changed"); changedArgs = e; };

        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        Assert.Equal(["changing", "changed"], order);
        Assert.Same(on, group.CurrentState);
        Assert.Null(changingArgs!.OldState);
        Assert.Same(on, changingArgs.NewState);
        Assert.Null(changingArgs.Control);
        Assert.Same(root, changingArgs.StateGroupsRoot);
        Assert.Same(on, changedArgs!.NewState);

        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(50.0, dial.Angle, precision: 9);

        Assert.True(VisualStateManager.GoToElementState(root, "Off", false));
        Assert.Same(off, group.CurrentState);
        Assert.Same(on, changedArgs!.OldState ?? off);
    }

    [Fact]
    public void Go_to_state_stops_the_previous_storyboard()
    {
        var dial = new Dial();
        var (root, group, _, on) = ArrangeGroup();
        var first = new VisualState { Name = "First", Storyboard = SpinBoard(dial, 100.0, 10.0) };
        on.Storyboard = SpinBoard(dial, 10.0, 10.0);
        group.States.Add(first);

        Assert.True(VisualStateManager.GoToElementState(root, "First", false));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(50.0, dial.Angle, precision: 9);
        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(5.0, dial.Angle, precision: 9);
    }

    [Fact]
    public void Timed_transition_begins_both_storyboards_with_sync_events()
    {
        // V1 boundary, pinned: WPF defers the state storyboard plus Changed
        // until a generated interpolation completes; the engine begins both
        // storyboards at once and fires both notices synchronously.
        var dial = new Dial();
        var (root, group, _, on) = ArrangeGroup();
        on.Storyboard = SpinBoard(dial, 10.0, 10.0);
        group.Transitions.Add(new VisualTransition
        {
            From = "Off",
            To = "On",
            GeneratedDuration = new Duration(TimeSpan.FromSeconds(2)),
        });
        var order = new List<string>();
        group.CurrentStateChanging += (_, _) => order.Add("changing");
        group.CurrentStateChanged += (_, _) => order.Add("changed");

        Assert.True(VisualStateManager.GoToElementState(root, "Off", false));
        Assert.True(VisualStateManager.GoToElementState(root, "On", true));
        Assert.Equal(["changing", "changed", "changing", "changed"], order);
        Assert.Same(on, group.CurrentState);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(5.0, dial.Angle, precision: 9);
    }

    [Fact]
    public void Get_transition_scores_exact_over_wildcard_with_default_fallback()
    {
        var (_, group, off, on) = ArrangeGroup();
        var any = new VisualTransition { GeneratedDuration = new Duration(TimeSpan.FromSeconds(1)) };
        var toOnly = new VisualTransition { To = "On" };
        var exact = new VisualTransition { From = "Off", To = "On" };
        var fallback = new VisualTransition();
        typeof(VisualTransition).GetProperty("IsDefault",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fallback, true);
        group.Transitions.Add(any);
        group.Transitions.Add(toOnly);
        group.Transitions.Add(exact);
        group.Transitions.Add(fallback);

        var root = new Border();
        Assert.Same(exact, VisualStateManager.GetTransition(root, group, off, on));
        Assert.Same(toOnly, VisualStateManager.GetTransition(root, group, null, on));
        Assert.Same(any, VisualStateManager.GetTransition(root, group, null, off));
        group.Transitions.Remove(exact);
        group.Transitions.Remove(toOnly);
        group.Transitions.Remove(any);
        Assert.Same(fallback, VisualStateManager.GetTransition(root, group, off, on));
        group.Transitions.Remove(fallback);
        Assert.Null(VisualStateManager.GetTransition(root, group, off, on));

        // Naming other existing states skips the transition entirely
        // (unknown names stay wildcards, like WPF, so this names Off's peer).
        var named = new VisualTransition { From = "On", To = "Off" };
        group.Transitions.Add(named);
        Assert.Null(VisualStateManager.GetTransition(root, group, off, on));
    }

    [Fact]
    public void Custom_manager_runs_even_for_unknown_states()
    {
        var (root, group, _, _) = ArrangeGroup();
        var custom = new RecordingManager { Result = false };
        VisualStateManager.SetCustomVisualStateManager(root, custom);
        Assert.False(VisualStateManager.GoToElementState(root, "Missing", false));
        Assert.Single(custom.Calls);
        Assert.Equal("Missing:nogroup:nostate", custom.Calls[0]);
        Assert.Null(group.CurrentState);

        custom.Result = true;
        Assert.True(VisualStateManager.GoToElementState(root, "Missing", false));
        Assert.Null(group.CurrentState);
    }

    [Fact]
    public void Custom_manager_override_binds_like_behaviors()
    {
        // ExtendedVisualStateManager overrides GoToStateCore; the override
        // must receive the resolved group/state through the base signature.
        var (root, _, _, on) = ArrangeGroup();
        var custom = new RecordingManager { Result = true };
        VisualStateManager.SetCustomVisualStateManager(root, custom);
        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        Assert.Equal("On:Common:On", custom.Calls[0]);
        Assert.Same(on, VisualStateManager.GetVisualStateGroups(root)[0] is VisualStateGroup g ? g.GetState("On") : null);
    }

    [Fact]
    public void Changed_args_carry_roots_and_have_no_public_ctor()
    {
        var root = new Border();
        var group = new VisualStateGroup { Name = "G" };
        var on = new VisualState { Name = "On" };
        group.States.Add(on);
        VisualStateManager.GetVisualStateGroups(root).Add(group);
        VisualStateChangedEventArgs? seen = null;
        group.CurrentStateChanged += (_, e) => seen = e;
        Assert.True(VisualStateManager.GoToElementState(root, "On", false));
        Assert.NotNull(seen);
        Assert.Null(seen!.OldState);
        Assert.Same(on, seen.NewState);
        Assert.Null(seen.Control);
        Assert.Same(root, seen.StateGroupsRoot);
        Assert.Empty(typeof(VisualStateChangedEventArgs).GetConstructors());
    }
}
