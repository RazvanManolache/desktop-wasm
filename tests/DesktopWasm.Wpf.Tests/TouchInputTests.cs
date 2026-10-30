using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-75 HandyControl entry touch scope: TouchAction values,
/// TouchPoint/Collection shapes, TouchDevice/TouchEventArgs parity plus the
/// pointer-path mapping (down mints a contact, move updates it, up retires
/// it; capture pins the route; positions read live). Member evidence via
/// Mono.Cecil on the upstream net10.0-windows HandyControlDemo.dll
/// (TouchDragMoveWindow ctor subscribes add_TouchDown/add_TouchUp,
/// MainWindow_TouchDown reads get_TouchDevice and calls
/// UIElement.CaptureTouch); shapes live-verified against the 9.0.20
/// PresentationCore ref pack (TouchAction 0-2, TouchEventArgs non-sealed
/// with a public (TouchDevice, int) ctor, TouchDevice abstract with a
/// protected (int) ctor).</summary>
[ResetAmbientState]
public sealed class TouchInputTests
{
    [Fact]
    public void TouchAction_values_match_wpf()
    {
        Assert.Equal(0, (int)TouchAction.Down);
        Assert.Equal(1, (int)TouchAction.Move);
        Assert.Equal(2, (int)TouchAction.Up);
    }

    [Fact]
    public void TouchDevice_is_abstract_with_protected_id_ctor()
    {
        Assert.True(typeof(TouchDevice).IsAbstract);
        Assert.True(typeof(TouchDevice).IsSubclassOf(typeof(DispatcherObject)));
        var ctor = typeof(TouchDevice).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            [typeof(int)]);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsFamily);
        Assert.True(typeof(TouchDevice).IsAssignableFrom(typeof(TestTouchDevice)));
    }

    [Fact]
    public void TouchEventArgs_public_ctor_lands_device_and_timestamp()
    {
        var device = new TestTouchDevice(7);
        var args = new TouchEventArgs(device, 123);
        Assert.Same(device, args.TouchDevice);
        Assert.Equal(123, args.Timestamp);
        Assert.False(typeof(TouchEventArgs).IsSealed);
        Assert.IsAssignableFrom<InputEventArgs>(args);
        Assert.Throws<ArgumentNullException>(() => new TouchEventArgs(null!, 0));
    }

    [Fact]
    public void TouchPoint_ctor_lands_parts_with_size_projection()
    {
        var device = new TestTouchDevice(3);
        var point = new TouchPoint(device, new Point(50, 15), new Rect(50, 15, 1, 1), TouchAction.Down);
        Assert.Same(device, point.TouchDevice);
        Assert.Equal(new Point(50, 15), point.Position);
        Assert.Equal(new Rect(50, 15, 1, 1), point.Bounds);
        Assert.Equal(new Size(1, 1), point.Size);
        Assert.Equal(TouchAction.Down, point.Action);
        Assert.Throws<ArgumentNullException>(() => new TouchPoint(null!, new Point(0, 0), new Rect(0, 0, 1, 1), TouchAction.Move));
    }

    [Fact]
    public void TouchPointCollection_empty_ctor_and_add()
    {
        var devices = new TestTouchDevice(1);
        var points = new TouchPointCollection();
        Assert.Empty(points);
        points.Add(new TouchPoint(devices, new Point(1, 2), new Rect(1, 2, 1, 1), TouchAction.Move));
        Assert.Single(points);
    }

    [Fact]
    public void TouchDevice_binds_imanipulator()
    {
        var device = new TestTouchDevice(11);
        var manipulator = Assert.IsAssignableFrom<IManipulator>(device);
        Assert.Equal(11, manipulator.Id);
        // No manager session behind an explicitly constructed device: the
        // untouched origin reads back, and a host-driven end is a silent
        // no-op while inactive.
        Assert.Equal(new Point(0, 0), manipulator.GetPosition(new Button()));
        manipulator.ManipulationEnded(true);
        Assert.False(device.IsActive);
    }

    private sealed class TestTouchDevice : TouchDevice
    {
        public TestTouchDevice(int id)
            : base(id)
        {
        }
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, WpfInputManager Manager, StackPanel Root, Button First, Button Second) TouchHarness()
    {
        // Panel root with explicit arrange (MouseBindingTests precedent): a
        // directly-constructed Window stays invisible until shown, so the
        // hit-tested pointer route would miss it.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var first = new Button { Content = "A", Width = 100, Height = 30 };
        var second = new Button { Content = "B", Width = 100, Height = 30 };
        root.AddChild(first);
        root.AddChild(second);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 300));
        root.Arrange(new WpfRect(0, 0, 400, 300));
        first.Arrange(new WpfRect(10, 10, 100, 30));
        second.Arrange(new WpfRect(150, 10, 100, 30));
        return (router, manager, root, first, second);
    }

    private static void Dispatch(WasmInputRouter router, double x, double y, WasmPointerKind kind, int pointerId) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, kind == WasmPointerKind.Down)
        {
            Kind = kind,
            PointerId = pointerId,
        });

    [Fact]
    public void Entry_drag_window_pattern_counts_contacts_through_pointer_dispatch()
    {
        // HandyControlDemo TouchDragMoveWindow logic: TouchDown captures the
        // contact and starts the drag when the first contact lands, TouchUp
        // releases the count.
        var (router, _, _, first, _) = TouchHarness();
        var count = 0;
        var drags = 0;
        first.TouchDown += (_, e) =>
        {
            Assert.True(first.CaptureTouch(e.TouchDevice));
            if (count == 0) drags++;
            count++;
        };
        first.TouchUp += (_, _) => count--;

        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.Equal(1, count);
        Assert.Equal(1, drags);
        Assert.True(first.AreAnyTouchesCaptured);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);
        Assert.Equal(0, count);
        Assert.False(first.AreAnyTouchesCaptured);
    }

    [Fact]
    public void Touch_preview_tunnels_before_bubble_and_handled_preview_suppresses_bubble()
    {
        var (router, _, root, first, _) = TouchHarness();
        var order = new List<string>();
        root.PreviewTouchDown += (_, _) => order.Add("root-preview");
        first.PreviewTouchDown += (_, _) => order.Add("first-preview");
        first.TouchDown += (_, _) => order.Add("first-bubble");
        root.TouchDown += (_, _) => order.Add("root-bubble");

        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.Equal(["root-preview", "first-preview", "first-bubble", "root-bubble"], order);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);

        order.Clear();
        first.PreviewTouchDown += (_, e) => e.Handled = true;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 2);
        Assert.Equal(["root-preview", "first-preview"], order);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 2);
    }

    [Fact]
    public void CaptureTouch_pins_move_route_until_released()
    {
        var (router, _, _, first, second) = TouchHarness();
        var firstMoves = 0;
        var secondMoves = 0;
        var got = 0;
        var lost = 0;
        first.TouchDown += (_, e) => first.CaptureTouch(e.TouchDevice);
        first.TouchMove += (_, _) => firstMoves++;
        second.TouchMove += (_, _) => secondMoves++;
        first.GotTouchCapture += (_, _) => got++;
        first.LostTouchCapture += (_, _) => lost++;

        TouchDevice? device = null;
        first.TouchDown += (_, e) => device = e.TouchDevice;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.Equal(1, got);
        Assert.NotNull(device);
        Assert.True(device!.IsActive);

        // Drag onto the sibling: the captured contact still reaches first.
        Dispatch(router, 200, 25, WasmPointerKind.Move, 1);
        Assert.Equal(1, firstMoves);
        Assert.Equal(0, secondMoves);

        Assert.True(first.ReleaseTouchCapture(device));
        Assert.Equal(1, lost);
        Dispatch(router, 200, 25, WasmPointerKind.Move, 1);
        Assert.Equal(1, firstMoves);
        Assert.Equal(1, secondMoves);
        Dispatch(router, 200, 25, WasmPointerKind.Up, 1);
    }

    [Fact]
    public void Move_without_contact_raises_nothing()
    {
        var (router, _, _, first, _) = TouchHarness();
        var events = 0;
        first.TouchDown += (_, _) => events++;
        first.TouchMove += (_, _) => events++;
        first.TouchEnter += (_, _) => events++;
        first.TouchLeave += (_, _) => events++;

        Dispatch(router, 60, 25, WasmPointerKind.Move, 1);
        Assert.Equal(0, events);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);
        Assert.Equal(0, events);
    }

    [Fact]
    public void GetTouchPoint_reads_live_element_relative_position()
    {
        var (router, _, _, first, _) = TouchHarness();
        TouchDevice? device = null;
        first.TouchDown += (_, e) => device = e.TouchDevice;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.NotNull(device);
        // First button is arranged at (10, 10): root (60, 25) lands (50, 15).
        Assert.Equal(new Point(50, 15), device!.GetTouchPoint(first).Position);
        Assert.Equal(device, device.GetTouchPoint(first).TouchDevice);
        Assert.Equal(TouchAction.Down, device.GetTouchPoint(first).Action);
        Dispatch(router, 70, 35, WasmPointerKind.Up, 1);
        Assert.Equal(new Point(60, 25), device.GetTouchPoint(first).Position);
        Assert.Equal(TouchAction.Up, device.GetTouchPoint(first).Action);
        Assert.False(device.IsActive);
    }

    [Fact]
    public void GetIntermediateTouchPoints_returns_session_history_oldest_first()
    {
        var (router, _, _, first, _) = TouchHarness();
        TouchDevice? device = null;
        first.TouchDown += (_, e) => device = e.TouchDevice;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Dispatch(router, 70, 25, WasmPointerKind.Move, 1);
        Dispatch(router, 80, 25, WasmPointerKind.Move, 1);
        Assert.NotNull(device);
        var history = device!.GetIntermediateTouchPoints(first);
        Assert.Equal(3, history.Count);
        Assert.Equal([TouchAction.Down, TouchAction.Move, TouchAction.Move],
            history.Select(p => p.Action));
        Assert.Equal([new Point(50, 15), new Point(60, 15), new Point(70, 15)],
            history.Select(p => p.Position));
        Dispatch(router, 80, 25, WasmPointerKind.Up, 1);
    }

    [Fact]
    public void Concurrent_contacts_stay_independent()
    {
        var (router, _, _, first, _) = TouchHarness();
        var downs = 0;
        var devices = new List<TouchDevice>();
        first.TouchDown += (_, e) => { downs++; devices.Add(e.TouchDevice); };
        Dispatch(router, 60, 25, WasmPointerKind.Down, 7);
        Dispatch(router, 70, 25, WasmPointerKind.Down, 9);
        Assert.Equal(2, downs);
        Assert.Equal([7, 9], devices.Select(d => d.Id));
        Assert.Equal(2, first.TouchesOver.Count());
        Assert.True(first.AreAnyTouchesOver);
        Assert.Equal(2, first.TouchesDirectlyOver.Count());
        Dispatch(router, 60, 25, WasmPointerKind.Up, 7);
        Assert.Single(first.TouchesOver);
        Assert.True(first.AreAnyTouchesOver);
        Dispatch(router, 70, 25, WasmPointerKind.Up, 9);
        Assert.Empty(first.TouchesOver);
        Assert.False(first.AreAnyTouchesOver);
    }

    [Fact]
    public void Up_releases_capture_and_clears_over_state()
    {
        var (router, _, root, first, _) = TouchHarness();
        var lost = 0;
        first.TouchDown += (_, e) => first.CaptureTouch(e.TouchDevice);
        first.LostTouchCapture += (_, _) => lost++;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.True(first.AreAnyTouchesCaptured);
        Assert.True(first.AreAnyTouchesCapturedWithin);
        Assert.True(root.AreAnyTouchesCapturedWithin);
        Assert.True(first.AreAnyTouchesOver);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);
        Assert.Equal(1, lost);
        Assert.False(first.AreAnyTouchesCaptured);
        Assert.False(first.AreAnyTouchesOver);
        Assert.Empty(first.TouchesCaptured);
    }

    [Fact]
    public void ReleaseAllTouchCaptures_drops_every_held_contact()
    {
        var (router, _, _, first, _) = TouchHarness();
        var lost = 0;
        first.TouchDown += (_, e) => first.CaptureTouch(e.TouchDevice);
        first.LostTouchCapture += (_, _) => lost++;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Dispatch(router, 70, 25, WasmPointerKind.Down, 2);
        Assert.Equal(2, first.TouchesCaptured.Count());
        first.ReleaseAllTouchCaptures();
        Assert.Equal(2, lost);
        Assert.Empty(first.TouchesCaptured);
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);
        Dispatch(router, 70, 25, WasmPointerKind.Up, 2);
    }

    [Fact]
    public void CaptureTouch_follows_device_owner_and_rejects_retired_and_stray_devices()
    {
        var (router, _, _, first, _) = TouchHarness();
        TouchDevice? device = null;
        first.TouchDown += (_, e) => device = e.TouchDevice;
        Dispatch(router, 60, 25, WasmPointerKind.Down, 1);
        Assert.NotNull(device);
        // Traffic on another manager flips the ambient Current, but capture
        // follows the contact's owning manager (the slice-74 cross-talk
        // rule), so it still lands.
        _ = new WpfInputManager(new WasmInputRouter());
        Assert.True(first.CaptureTouch(device!));
        Dispatch(router, 60, 25, WasmPointerKind.Up, 1);
        Assert.False(device!.IsActive);
        // A retired contact never recaptures, and neither does a device
        // that was never dispatched through any manager.
        Assert.False(first.CaptureTouch(device));
        Assert.False(first.CaptureTouch(new TestTouchDevice(99)));
    }
}
