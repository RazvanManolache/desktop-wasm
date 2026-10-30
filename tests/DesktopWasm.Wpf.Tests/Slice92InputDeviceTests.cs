using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-92 AvalonEdit device-pair regression tests: InputDevice +
/// StylusDevice ride the default System.Windows.* rule once the engine
/// declares the exact upstream shapes (member shapes verified with Mono.Cecil
/// against the 10.0.13 PresentationCore ref pack). Converted call sites:
/// TextView.RaiseHoverEventPair (get_StylusDevice plus the public
/// (MouseDevice, int, StylusDevice) construction) and
/// CompletionWindow.textArea_PreviewTextInput plus TextArea.PerformTextInput
/// (InputEventArgs.get_Device plus the TextCompositionEventArgs composition
/// ctor, re-pinned to the slice-93 advance). No Application state is touched,
/// so these stay outside the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice92InputDeviceTests
{
    [Fact]
    public void InputDevice_shape_matches_live()
    {
        Assert.True(typeof(InputDevice).IsAbstract);
        Assert.False(typeof(InputDevice).IsSealed);
        Assert.Same(typeof(DispatcherObject), typeof(InputDevice).BaseType);
        var ctor = typeof(InputDevice).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsFamily);
        foreach (var name in new[] { "Target", "ActiveSource" })
        {
            var prop = typeof(InputDevice).GetProperty(name)!;
            Assert.NotNull(prop);
            Assert.True(prop!.GetGetMethod()!.IsAbstract);
            Assert.True(prop.GetGetMethod()!.IsVirtual);
            Assert.Null(prop.GetSetMethod(true));
        }
        Assert.Equal(typeof(IInputElement), typeof(InputDevice).GetProperty("Target")!.PropertyType);
        Assert.Equal(typeof(PresentationSource), typeof(InputDevice).GetProperty("ActiveSource")!.PropertyType);
    }

    [Fact]
    public void StylusDevice_shape_matches_live()
    {
        Assert.True(typeof(StylusDevice).IsSealed);
        Assert.False(typeof(StylusDevice).IsAbstract);
        Assert.Same(typeof(InputDevice), typeof(StylusDevice).BaseType);
        Assert.Null(typeof(StylusDevice).GetConstructor(Type.EmptyTypes));
        var ctor = typeof(StylusDevice).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsAssembly);
        foreach (var name in new[] { "Target", "ActiveSource" })
        {
            var prop = typeof(StylusDevice).GetProperty(name)!;
            Assert.NotNull(prop);
            Assert.False(prop!.GetGetMethod()!.IsAbstract);
            Assert.True(prop.GetGetMethod()!.IsVirtual);
        }
    }

    [Fact]
    public void Stylus_hardware_surface_stays_follow_up()
    {
        // Boundary pin: the engine drives no pen hardware, so the sealed
        // StylusDevice carries the live inheritance/override shape with null
        // device state while the TabletDevice/StylusButtonCollection/Capture/
        // GetStylusPoints hardware surface stays a follow-up rather than a
        // dead shim.
        var device = NewStylusDevice();
        Assert.Null(device.Target);
        Assert.Null(device.ActiveSource);
        Assert.Null(typeof(StylusDevice).GetProperty("StylusButtons"));
        Assert.Null(typeof(StylusDevice).GetProperty("TabletDevice"));
        Assert.Null(typeof(StylusDevice).GetProperty("Captured"));
        Assert.Null(typeof(StylusDevice).GetProperty("DirectlyOver"));
        Assert.Null(typeof(StylusDevice).GetProperty("Id"));
        Assert.Null(typeof(StylusDevice).GetProperty("Name"));
        Assert.Null(typeof(StylusDevice).GetProperty("InAir"));
        Assert.Null(typeof(StylusDevice).GetProperty("InRange"));
        Assert.Null(typeof(StylusDevice).GetProperty("Inverted"));
        Assert.Null(typeof(StylusDevice).GetProperty("IsValid"));
        foreach (var name in new[] { "Capture", "GetPosition", "GetStylusPoints", "Synchronize" })
            Assert.Null(typeof(StylusDevice).GetMethod(name));
    }

    [Fact]
    public void Sealed_devices_rebase_onto_input_device()
    {
        Assert.Same(typeof(InputDevice), typeof(MouseDevice).BaseType);
        Assert.Same(typeof(InputDevice), typeof(KeyboardDevice).BaseType);
        Assert.True(typeof(MouseDevice).IsSealed);
        Assert.True(typeof(KeyboardDevice).IsSealed);
        // Composition sources stay null: the engine composes no Win32 target.
        // Isolated instances: never touch the ambient singletons here.
        Assert.Null(new MouseDevice().ActiveSource);
        Assert.Null(new KeyboardDevice().ActiveSource);
    }

    [Fact]
    public void Mouse_target_tracks_directly_over()
    {
        // Isolated instance: Mouse.PrimaryDevice.Target routes to the
        // ambient DirectlyOverElement, which parallel test classes mutate,
        // so assert the routing on a device this test owns.
        var device = new MouseDevice();
        Assert.Null(device.Target);
        var element = new StubBox();
        device.DirectlyOver = element;
        Assert.Same(element, device.Target);
        Assert.Same(device.DirectlyOver, device.Target);
        device.DirectlyOver = null;
        Assert.Null(device.Target);
    }

    [Fact]
    public void Keyboard_target_carries_focus_through_set_target()
    {
        // The override stays get-only like live; the focus manager writes
        // through SetTarget (RoutedInput focus path). Isolated instance:
        // the PrimaryDevice singleton is shared process state mutated by
        // parallel focus tests (RoutedInput SetFocused -> SetTarget).
        Assert.Null(typeof(KeyboardDevice).GetProperty("Target")!.GetSetMethod(true));
        var device = new KeyboardDevice();
        Assert.Null(device.Target);
        var element = new StubBox();
        device.SetTarget(element);
        Assert.Same(element, device.Target);
        Assert.Same(element, device.FocusedElement);
        device.SetTarget(null);
        Assert.Null(device.Target);
    }

    [Fact]
    public void Device_routes_through_key_and_mouse_args()
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, Environment.TickCount,
            Key.A, Key.None, true, false);
        Assert.Same(Keyboard.PrimaryDevice, key.Device);
        Assert.Same(Keyboard.PrimaryDevice, key.KeyboardDevice);
        var mouse = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount);
        Assert.Same(Mouse.PrimaryDevice, mouse.Device);
        Assert.Same(Mouse.PrimaryDevice, mouse.MouseDevice);
        Assert.Null(mouse.StylusDevice);
    }

    [Fact]
    public void Device_defaults_to_null_on_device_less_args()
    {
        // The engine raises manipulation/touch args itself with no device
        // behind them (no touch hardware, no OS text services).
        Assert.Null(new ManipulationStartedEventArgs(new StubBox(), new Point(1, 2)).Device);
        Assert.Null(new TouchEventArgs(new StubTouchDevice(1), Environment.TickCount).Device);
    }

    [Fact]
    public void Stylus_plumbing_matches_converted_raise_hover()
    {
        // Mirrors the converted TextView.RaiseHoverEventPair construction:
        // both live public overloads exist, the stylus lands on the args,
        // and Device still reports the mouse.
        var plain = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount);
        Assert.Null(plain.StylusDevice);
        var stylus = NewStylusDevice();
        var raised = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount, stylus);
        Assert.Same(stylus, raised.StylusDevice);
        Assert.Same(Mouse.PrimaryDevice, raised.Device);
        Assert.Same(Mouse.PrimaryDevice, raised.MouseDevice);
    }

    [Fact]
    public void QueryCursor_stylus_overload_matches_live()
    {
        // Closes the slice-89 follow-up: the public
        // (MouseDevice, int, StylusDevice) shape exists and lands the stylus
        // while Device still reports the mouse.
        var ctor = typeof(QueryCursorEventArgs).GetConstructor(
            [typeof(MouseDevice), typeof(int), typeof(StylusDevice)])!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPublic);
        var args = new QueryCursorEventArgs(
            Mouse.PrimaryDevice, Environment.TickCount, NewStylusDevice());
        Assert.NotNull(args.StylusDevice);
        Assert.Same(Mouse.PrimaryDevice, args.Device);
        Assert.Null(args.Cursor);
    }

    [Fact]
    public void Composition_ctor_binds()
    {
        // Re-pinned to the slice-93 advance: the converted
        // PerformTextInput/textArea_PreviewTextInput composition news
        // (TextCompositionEventArgs..ctor(InputDevice, TextComposition)) binds
        // onto the engine contract with the device flowing through
        // InputEventArgs.Device and the text through the stored composition.
        var ctor = typeof(TextCompositionEventArgs).GetConstructor(
            [typeof(InputDevice), typeof(TextComposition)])!;
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPublic);
        var composition = new TextComposition(InputManager.Current, new StubBox(), "hi");
        var args = (TextCompositionEventArgs)ctor.Invoke([Keyboard.PrimaryDevice, composition]);
        Assert.Same(Keyboard.PrimaryDevice, args.Device);
        Assert.Same(composition, args.TextComposition);
        Assert.Equal("hi", args.Text);
        Assert.NotNull(typeof(TextCompositionEventArgs).GetProperty("Device"));
    }

    private static StylusDevice NewStylusDevice() =>
        (StylusDevice)typeof(StylusDevice)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!
            .Invoke(null);

    private sealed class StubBox : FrameworkElement
    {
        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }
    }

    private sealed class StubTouchDevice : TouchDevice
    {
        public StubTouchDevice(int id)
            : base(id)
        {
        }
    }
}
