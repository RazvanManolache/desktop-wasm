using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-74 HandyControl mouse scope: MouseAction values, MouseGesture
/// parts/matching, MouseBinding two-way sync plus routed press/wheel dispatch
/// (mirrors the slice-60 KeyBinding surface for the HandyControl ImageViewer
/// move binding, ToggleBlock toggle gesture and SimpleMouseBinding sync).
/// Member evidence via Mono.Cecil on the upstream net8.0-windows
/// HandyControl.dll: MouseGesture..ctor(MouseAction),
/// get/set_MouseAction, MouseBinding..ctor(ICommand, MouseGesture);
/// shapes live-verified against the 9.0.21 PresentationCore ref pack
/// (MouseAction 0..7, MouseBinding.MouseActionProperty DP, non-sealed
/// classes, no DP on the gesture).</summary>
[ResetAmbientState]
public sealed class MouseBindingTests
{
    [Fact]
    public void MouseAction_values_match_wpf()
    {
        Assert.Equal(0, (int)MouseAction.None);
        Assert.Equal(1, (int)MouseAction.LeftClick);
        Assert.Equal(2, (int)MouseAction.RightClick);
        Assert.Equal(3, (int)MouseAction.MiddleClick);
        Assert.Equal(4, (int)MouseAction.WheelClick);
        Assert.Equal(5, (int)MouseAction.LeftDoubleClick);
        Assert.Equal(6, (int)MouseAction.RightDoubleClick);
        Assert.Equal(7, (int)MouseAction.MiddleDoubleClick);
    }

    [Fact]
    public void MouseBinding_defaults_and_dp_identity()
    {
        var binding = new MouseBinding();
        Assert.Equal(MouseAction.None, binding.MouseAction);
        Assert.Null(binding.Command);
        Assert.Null(binding.CommandParameter);
        Assert.Null(binding.Gesture);
        Assert.Same(MouseBinding.MouseActionProperty, DependencyProperty.FromName(typeof(MouseBinding), "MouseAction"));
        Assert.IsAssignableFrom<InputBinding>(binding);
    }

    [Fact]
    public void MouseBinding_command_gesture_ctor_lands_both()
    {
        var command = new RelayCommand(_ => { });
        var gesture = new MouseGesture(MouseAction.LeftClick);
        var binding = new MouseBinding(command, gesture);
        Assert.Same(command, binding.Command);
        Assert.Same(gesture, binding.Gesture);
        Assert.Equal(MouseAction.LeftClick, binding.MouseAction);
    }

    [Fact]
    public void MouseBinding_subclass_chains_base_ctors_like_simple_mouse_binding()
    {
        var derived = new DerivedMouseBinding(new RelayCommand(_ => { }), new MouseGesture(MouseAction.RightClick));
        Assert.Equal(MouseAction.RightClick, derived.MouseAction);
        Assert.IsType<MouseGesture>(derived.Gesture);
    }

    private sealed class DerivedMouseBinding : MouseBinding
    {
        public DerivedMouseBinding()
        {
        }

        public DerivedMouseBinding(ICommand command, MouseGesture gesture)
            : base(command, gesture)
        {
        }

        protected override Freezable CreateInstanceCore() => new DerivedMouseBinding();
    }

    [Fact]
    public void MouseBinding_syncs_action_and_gesture_both_ways()
    {
        var binding = new MouseBinding { Command = new RelayCommand(_ => { }) };
        binding.MouseAction = MouseAction.MiddleClick;
        var gesture = Assert.IsType<MouseGesture>(binding.Gesture);
        Assert.Equal(MouseAction.MiddleClick, gesture.MouseAction);

        binding.Gesture = new MouseGesture(MouseAction.WheelClick, ModifierKeys.Control);
        Assert.Equal(MouseAction.WheelClick, binding.MouseAction);

        // A non-mouse gesture is stored but leaves the action alone (KeyBinding parity).
        binding.Gesture = new KeyGesture(Key.N, ModifierKeys.Control);
        Assert.Equal(MouseAction.WheelClick, binding.MouseAction);
    }

    [Fact]
    public void MouseBinding_clone_keeps_runtime_type()
    {
        var binding = new MouseBinding(new RelayCommand(_ => { }), new MouseGesture(MouseAction.LeftClick));
        Assert.IsType<MouseBinding>(binding.Clone());
    }

    [Fact]
    public void Button_args_public_ctor_matches_native_down_shape()
    {
        // Native parity for the converted ListBoxAssist re-raise shape
        // (MouseDevice, timestamp, button): down-shaped like native
        // construction, and recognized by press gestures.
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
        Assert.Equal(MouseButton.Left, args.ChangedButton);
        Assert.Equal(MouseButtonState.Pressed, args.ButtonState);
        Assert.Equal(1, args.ClickCount);
        Assert.Equal(MouseButtonState.Pressed, args.LeftButton);
        Assert.Equal(MouseButtonState.Released, args.RightButton);
        Assert.True(new MouseGesture(MouseAction.LeftClick).Matches(new Button(), args));
        var right = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right);
        Assert.Equal(MouseButtonState.Released, right.LeftButton);
        Assert.Equal(MouseButtonState.Pressed, right.RightButton);
    }

    private static MouseButtonEventArgs PressArgs(MouseButton button, int clicks = 1) =>
        new(Mouse.PrimaryDevice, 0,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released,
            button, MouseButtonState.Pressed, clicks);

    private static MouseWheelEventArgs WheelArgs() =>
        new(Mouse.PrimaryDevice, 0,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, 120);

    [Fact]
    public void MouseGesture_matches_press_release_wheel_and_modifiers()
    {
        var prior = Keyboard.PrimaryDevice.Modifiers;
        try
        {
            Keyboard.PrimaryDevice.Modifiers = ModifierKeys.None;
            var left = new MouseGesture(MouseAction.LeftClick);
            Assert.True(left.Matches(new Button(), PressArgs(MouseButton.Left)));
            Assert.False(left.Matches(new Button(), PressArgs(MouseButton.Right)));
            Assert.False(left.Matches(new Button(), PressArgs(MouseButton.Left, clicks: 2)));
            Assert.True(new MouseGesture(MouseAction.LeftDoubleClick).Matches(new Button(), PressArgs(MouseButton.Left, clicks: 2)));
            // Releases never match (gestures fire on press, like WPF).
            Assert.False(left.Matches(new Button(), new MouseButtonEventArgs(Mouse.PrimaryDevice, 0,
                MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released,
                MouseButton.Left, MouseButtonState.Released, 1)));
            Assert.False(left.Matches(new Button(), WheelArgs()));
            Assert.True(new MouseGesture(MouseAction.WheelClick).Matches(new Button(), WheelArgs()));
            Assert.False(new MouseGesture(MouseAction.LeftClick).Matches(new Button(),
                new KeyEventArgs(Keyboard.PrimaryDevice, 0, Key.A, Key.None, true, false)));

            Keyboard.PrimaryDevice.Modifiers = ModifierKeys.Control;
            Assert.False(left.Matches(new Button(), PressArgs(MouseButton.Left)));
            Assert.True(new MouseGesture(MouseAction.LeftClick, ModifierKeys.Control).Matches(new Button(), PressArgs(MouseButton.Left)));
        }
        finally
        {
            Keyboard.PrimaryDevice.Modifiers = prior;
        }
    }

    [Fact]
    public void InputBindingCollection_find_match_routes_mouse_gestures()
    {
        var prior = Keyboard.PrimaryDevice.Modifiers;
        try
        {
            Keyboard.PrimaryDevice.Modifiers = ModifierKeys.None;
            var bindings = new InputBindingCollection
            {
                new MouseBinding(new RelayCommand(_ => { }), new MouseGesture(MouseAction.RightClick)),
            };
            var target = new Button();
            Assert.NotNull(bindings.FindMatch(target, PressArgs(MouseButton.Right)));
            Assert.Null(bindings.FindMatch(target, PressArgs(MouseButton.Left)));
        }
        finally
        {
            Keyboard.PrimaryDevice.Modifiers = prior;
        }
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, WpfInputManager Manager, StackPanel Root, Button Button) MouseHarness()
    {
        // Panel root with explicit arrange (CommandTests precedent): a
        // directly-constructed Window stays invisible until shown, so the
        // hit-tested pointer route would miss it.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var button = new Button { Content = "Go", Width = 100, Height = 30 };
        root.AddChild(button);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 300));
        root.Arrange(new WpfRect(0, 0, 400, 300));
        button.Arrange(new WpfRect(10, 10, 100, 30));
        return (router, manager, root, button);
    }

    private const double PressX = 60;
    private const double PressY = 25;

    private static void PressAt(WasmInputRouter router, WasmPointerButton button = WasmPointerButton.Left)
    {
        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, button, true)
        {
            Kind = WasmPointerKind.Down,
            PointerId = 1,
        });
        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, button, false)
        {
            Kind = WasmPointerKind.Up,
            PointerId = 1,
        });
    }

    [Fact]
    public void Left_click_binding_executes_and_bubble_ran_first()
    {
        var (router, _, _, button) = MouseHarness();
        var calls = new List<object?>();
        var downs = 0;
        button.MouseDown += (_, _) => downs++;
        button.InputBindings.Add(new MouseBinding(
            new RelayCommand(p => calls.Add(p), _ => true),
            new MouseGesture(MouseAction.LeftClick)) { CommandParameter = "move-view" });

        PressAt(router);
        Assert.Equal(["move-view"], calls);
        Assert.Equal(1, downs); // bubble ran first; the binding consumed it after

        calls.Clear();
        PressAt(router, WasmPointerButton.Right);
        Assert.Empty(calls);
    }

    [Fact]
    public void Double_click_press_maps_to_double_action_only()
    {
        var (router, _, _, button) = MouseHarness();
        var singles = 0;
        var doubles = 0;
        button.InputBindings.Add(new MouseBinding(new RelayCommand(_ => singles++), new MouseGesture(MouseAction.LeftClick)));
        button.InputBindings.Add(new MouseBinding(new RelayCommand(_ => doubles++), new MouseGesture(MouseAction.LeftDoubleClick)));

        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Down, PointerId = 1 });
        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, WasmPointerButton.Left, false) { Kind = WasmPointerKind.Up, PointerId = 1 });
        Assert.Equal(1, singles);
        Assert.Equal(0, doubles);
        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Down, PointerId = 2 });
        router.DispatchPointer(new WasmPointerArgs(Handle, PressX, PressY, WasmPointerButton.Left, false) { Kind = WasmPointerKind.Up, PointerId = 2 });
        Assert.Equal(1, singles);
        Assert.Equal(1, doubles);
    }

    [Fact]
    public void Wheel_binding_executes_on_tick()
    {
        var (router, _, _, button) = MouseHarness();
        var ticks = 0;
        button.InputBindings.Add(new MouseBinding(
            new RelayCommand(_ => ticks++), new MouseGesture(MouseAction.WheelClick)));
        router.DispatchWheel(new WasmWheelArgs(Handle, PressX, PressY, 120));
        Assert.Equal(1, ticks);
    }

    [Fact]
    public void Dispatched_press_ignores_ambient_modifiers_from_other_managers()
    {
        // Deterministic repro of the slice-74 parallel-suite failure: key
        // traffic on another manager leaves ambient Control behind and used
        // to veto this manager's None-modifier gestures mid-dispatch. The
        // routed press carries this manager's own modifier state (None: it
        // never saw a key event), so the gesture still matches.
        var (router, _, _, button) = MouseHarness();
        var calls = 0;
        button.InputBindings.Add(new MouseBinding(
            new RelayCommand(_ => calls++), new MouseGesture(MouseAction.LeftClick)));
        var prior = Keyboard.PrimaryDevice.Modifiers;
        try
        {
            var noisyRouter = new WasmInputRouter();
            _ = new WpfInputManager(noisyRouter);
            noisyRouter.DispatchKeyboard(new WasmKeyArgs(Handle, "n", true, true, false, false));
            Assert.Equal(ModifierKeys.Control, Keyboard.PrimaryDevice.Modifiers);
            PressAt(router);
            Assert.Equal(1, calls);
        }
        finally
        {
            Keyboard.PrimaryDevice.Modifiers = prior;
        }
    }

    [Fact]
    public void Mouse_binding_without_can_execute_stays_silent()
    {
        var (router, _, _, button) = MouseHarness();
        var calls = 0;
        button.InputBindings.Add(new MouseBinding(
            new RelayCommand(_ => calls++, _ => false), new MouseGesture(MouseAction.LeftClick)));
        PressAt(router);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Ancestor_mouse_binding_covers_hit_child_like_wpf()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var panel = new StackPanel();
        var inner = new Button { Content = "Inner", Width = 80, Height = 24 };
        panel.AddChild(inner);
        manager.AddRoot(panel);
        panel.Measure(new WpfSize(400, 300));
        panel.Arrange(new WpfRect(0, 0, 400, 300));
        inner.Arrange(new WpfRect(0, 0, 80, 24));

        var calls = 0;
        panel.InputBindings.Add(new MouseBinding(
            new RelayCommand(_ => calls++), new MouseGesture(MouseAction.LeftClick)));
        router.DispatchPointer(new WasmPointerArgs(Handle, 40, 12, WasmPointerButton.Left, true)
        {
            Kind = WasmPointerKind.Down,
            PointerId = 1,
        });
        Assert.Equal(1, calls);
    }
}
