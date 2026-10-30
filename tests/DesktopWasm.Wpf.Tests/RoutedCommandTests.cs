namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Routed commands (Step 40): element/class binding routing, preview and
/// bubble order, can-execute gating, requery broadcast, stock commands,
/// system commands and Button integration.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class RoutedCommandTests : IDisposable
{
    public RoutedCommandTests()
    {
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class ProbeBox : FrameworkElement
    {
        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }
    }

    [Fact]
    public void Two_arg_binding_defaults_can_execute_true()
    {
        // WPFGallery Copy bindings shape: CommandBinding(command, executed).
        var box = new ProbeBox();
        var copy = new RoutedCommand("Copy", typeof(ProbeBox));
        var ran = 0;
        box.CommandBindings.Add(new CommandBinding(copy, (_, e) => { ran++; e.Handled = true; }));
        Assert.True(copy.CanExecute(null, box));
        copy.Execute(null, box);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Execute_fires_matching_binding_and_stops_when_handled()
    {
        var box = new ProbeBox();
        var save = new RoutedCommand("Save", typeof(ProbeBox));
        var log = new List<string>();
        box.CommandBindings.Add(new CommandBinding(save,
            (_, e) => { log.Add($"executed:{e.Parameter}"); e.Handled = true; },
            (_, e) => { e.CanExecute = true; }));

        save.Execute("doc", box);
        Assert.Equal(["executed:doc"], log);
        Assert.True(save.CanExecute("doc", box));
    }

    [Fact]
    public void Preview_runs_before_bubble_in_route_order()
    {
        var parent = new ProbeBox();
        var child = new ProbeBox();
        parent.AddChild(child);
        var cmd = new RoutedCommand("Go", typeof(ProbeBox));
        var order = new List<string>();
        child.CommandBindings.Add(new CommandBinding(cmd,
            (_, e) => order.Add("child-bubble"),
            (_, e) => e.CanExecute = true));
        child.CommandBindings[0].PreviewExecuted += (_, _) => order.Add("child-preview");
        parent.CommandBindings.Add(new CommandBinding(cmd,
            (_, e) => { order.Add("parent-bubble"); e.Handled = true; },
            (_, e) => e.CanExecute = true));

        cmd.Execute(null, child);
        Assert.Equal(["child-preview", "child-bubble", "parent-bubble"], order);
    }

    [Fact]
    public void Class_bindings_fire_for_derived_targets()
    {
        var box = new ProbeBox();
        var cmd = new RoutedCommand("Classy", typeof(ProbeBox));
        var ran = 0;
        var binding = new CommandBinding(cmd,
            (_, e) => { ran++; e.Handled = true; },
            (_, e) => e.CanExecute = true);
        CommandManager.RegisterClassCommandBinding(typeof(ProbeBox), binding);
        try
        {
            cmd.Execute(null, box);
            Assert.Equal(1, ran);
        }
        finally { CommandManager.UnregisterClassCommandBinding(typeof(ProbeBox), binding); }

        cmd.Execute(null, box);
        Assert.Equal(1, ran);
        Assert.False(cmd.CanExecute(null, box));
    }

    [Fact]
    public void Missing_binding_is_a_quiet_noop()
    {
        var box = new ProbeBox();
        var cmd = new RoutedCommand("Lonely", typeof(ProbeBox));
        cmd.Execute(null, box);
        Assert.False(cmd.CanExecute(null, box));
        Assert.False(cmd.CanExecute(null, null));
    }

    [Fact]
    public void Requery_broadcast_reaches_commands()
    {
        var cmd = new RoutedCommand("Requery", typeof(ProbeBox));
        var raised = 0;
        cmd.CanExecuteChanged += (_, _) => raised++;
        CommandManager.InvalidateRequerySuggested();
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Stock_commands_carry_text_and_gestures()
    {
        Assert.Same(ApplicationCommands.Copy, ApplicationCommands.Copy);
        Assert.Equal("Copy", ApplicationCommands.Copy.Text);
        Assert.Equal("Copy", ApplicationCommands.Copy.Name);
        Assert.Equal("Not A Command", ApplicationCommands.NotACommand.Text);

        var custom = new RoutedUICommand("Save All", "SaveAll", typeof(ProbeBox),
            new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift) });
        Assert.Equal("Save All", custom.Text);
        Assert.Single(custom.InputGestures);
    }

    [Fact]
    public void System_commands_close_but_have_no_chrome()
    {
        SystemCommands.CloseWindow(null);
        Assert.Throws<InvalidOperationException>(() => SystemCommands.MaximizeWindow(null));
        Assert.Throws<InvalidOperationException>(() => SystemCommands.MinimizeWindow(null));
        Assert.Throws<InvalidOperationException>(() => SystemCommands.RestoreWindow(null));
        Assert.Throws<InvalidOperationException>(() => SystemCommands.ShowSystemMenu(null, new Point(0, 0)));
    }

    [Fact]
    public void Button_routes_routed_commands_from_itself()
    {
        var button = new Button();
        var save = new RoutedCommand("Save", typeof(Button));
        string? seen = null;
        button.CommandBindings.Add(new CommandBinding(save,
            (_, e) => { seen = (string?)e.Parameter; e.Handled = true; },
            (_, e) => e.CanExecute = true));
        button.Command = save;
        button.CommandParameter = "clicked";
        button.CommandTarget = button;
        button.RaiseClick();
        Assert.Equal("clicked", seen);
    }
}
