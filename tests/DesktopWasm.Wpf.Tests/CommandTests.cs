using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ICommand v1: RelayCommand, Button.Command execution gating, CanExecute grey-out, IsEnabled.</summary>
[ResetAmbientState]
public sealed class CommandTests
{
    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public StackPanel Root { get; } = new();
        public Button Button { get; } = new() { Content = "Go" };

        public Harness()
        {
            Manager = new WpfInputManager(Router);
            Root.AddChild(Button);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
            Button.Arrange(new WpfRect(10, 10, 200, 30));
        }

        public void Click(float x = 100, float y = 20)
        {
            var handle = new WasmWindowHandle(1);
            Router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true)
                { Kind = WasmPointerKind.Down, PointerId = 1 });
            Router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false)
                { Kind = WasmPointerKind.Up, PointerId = 1 });
        }
    }

    [Fact]
    public void RelayCommand_executes_when_allowed()
    {
        var ran = 0;
        var cmd = new RelayCommand(_ => ran++);
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void RelayCommand_blocks_when_disallowed()
    {
        var ran = 0;
        var cmd = new RelayCommand(_ => ran++, _ => false);
        Assert.False(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void RelayCommand_forwards_parameter()
    {
        object? seen = null;
        var cmd = new RelayCommand(p => seen = p);
        cmd.Execute("hi");
        Assert.Equal("hi", seen);
    }

    [Fact]
    public void Button_click_executes_bound_command_with_parameter()
    {
        var h = new Harness();
        object? seen = null;
        var clicks = 0;
        h.Button.Click += (_, _) => clicks++;
        h.Button.CommandParameter = "P";
        h.Button.Command = new RelayCommand(p => seen = p);
        h.Click();
        Assert.Equal(1, clicks);
        Assert.Equal("P", seen);
    }

    [Fact]
    public void Button_blocked_by_command_ignores_click()
    {
        var h = new Harness();
        var clicks = 0;
        var ran = 0;
        h.Button.Click += (_, _) => clicks++;
        h.Button.Command = new RelayCommand(_ => ran++, _ => false);
        h.Click();
        Assert.Equal(0, clicks);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void Button_reenables_on_can_execute_changed()
    {
        var h = new Harness();
        var clicks = 0;
        var allowed = false;
        var cmd = new RelayCommand(_ => { }, _ => allowed);
        h.Button.Click += (_, _) => clicks++;
        h.Button.Command = cmd;
        h.Click();
        Assert.Equal(0, clicks); // blocked
        allowed = true;
        cmd.RaiseCanExecuteChanged();
        h.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Disabled_button_ignores_click_and_focus()
    {
        var h = new Harness();
        var clicks = 0;
        h.Button.Click += (_, _) => clicks++;
        h.Button.IsEnabled = false;
        Assert.False(h.Button.IsEffectivelyEnabled);
        h.Click();
        Assert.Equal(0, clicks);
        Assert.False(h.Manager.TryFocus(h.Button));
        h.Button.IsEnabled = true;
        Assert.True(h.Button.IsEffectivelyEnabled);
        h.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Command_parameter_change_requeries_availability()
    {
        var h = new Harness();
        var clicks = 0;
        h.Button.Click += (_, _) => clicks++;
        h.Button.Command = new RelayCommand(_ => { }, p => (string?)p == "go");
        h.Button.CommandParameter = "stop";
        h.Click();
        Assert.Equal(0, clicks);
        h.Button.CommandParameter = "go";
        h.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Blocked_button_renders_greyed()
    {
        static uint FillOf(Button b)
        {
            b.Measure(new WpfSize(800, 600));
            b.Arrange(new WpfRect(0, 0, 200, 30));
            return WasmMilCore.RenderHeadless(b, new WpfSize(800, 600))
                .OfType<DesktopWasm.Graphics.DrawCommand.FillRect>().ElementAt(1).Rgba;
        }

        var live = new Button { Content = "x" };
        var blocked = new Button { Content = "x", Command = new RelayCommand(_ => { }, _ => false) };
        Assert.NotEqual(FillOf(live), FillOf(blocked));
        Assert.Equal(0xCCCCCCFFu, FillOf(blocked));
    }

    [Fact]
    public void Keyboard_activation_honors_command_gate()
    {
        var h = new Harness();
        Assert.True(h.Manager.TryFocus(h.Button));
        var clicks = 0;
        h.Button.Click += (_, _) => clicks++;
        h.Button.Command = new RelayCommand(_ => { }, _ => false);
        h.Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "Enter", true, false, false, false));
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Xaml_command_binding_executes()
    {
        var window = (Window)XamlLoader.Load(
            """<Window><Button Content="Go" Command="{Binding Save}"/></Window>""").Root;
        var button = (Button)window.Content!;
        var ran = 0;
        window.DataContext = new { Save = new RelayCommand(_ => ran++) };
        Assert.Equal("Go", button.Content);
        button.RaiseClick();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Xaml_literal_command_is_precise_error()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            """<Window><Button Content="Go" Command="Save"/></Window>"""));
        Assert.Contains("namescope", ex.Message);
    }

    [Fact]
    public void Xaml_command_parameter_sets_plain_value()
    {
        var window = (Window)XamlLoader.Load(
            """<Window><Button Content="Go" CommandParameter="P"/></Window>""").Root;
        Assert.Equal("P", ((Button)window.Content!).CommandParameter);
    }

    [Fact]
    public void Replacing_command_unsubscribes_old_availability()
    {
        var button = new Button { Content = "x" };
        var oldRaises = 0;
        var old = new RelayCommand(_ => { }, _ => false);
        old.CanExecuteChanged += (_, _) => oldRaises++;
        button.Command = old;
        Assert.False(button.IsEffectivelyEnabled);
        button.Command = new RelayCommand(_ => { });
        Assert.True(button.IsEffectivelyEnabled);
        old.RaiseCanExecuteChanged();
        Assert.Equal(1, oldRaises); // the event fired, but the button ignores it now
        Assert.True(button.IsEffectivelyEnabled);
    }
}
