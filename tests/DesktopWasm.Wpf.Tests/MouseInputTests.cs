using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Mouse input (Step 39): CLR mouse events around the Wasm route, hover
/// enter/leave transitions, button state, capture, click counting,
/// cursor statics, enum values and traversal requests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class MouseInputTests : IDisposable
{
    private readonly WasmInputRouter _router = new();
    private readonly WpfInputManager _manager;

    public MouseInputTests()
    {
        WpfAnimationClock.ResetForTests();
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        _manager = new WpfInputManager(_router);
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    private sealed class ProbeBox : FrameworkElement
    {
        public ProbeBox(double x, double y, double w, double h) => Frame = new WpfRect(x, y, w, h);

        public WpfRect Frame { get; }

        public override bool Focusable => true;
        protected override WpfSize MeasureCore(WpfSize available) => new(Frame.Width, Frame.Height);
        protected override void ArrangeCore(WpfRect finalRect) { }
        protected override void OnRender(DrawingContext dc) { }

        public void Layout()
        {
            Measure(new WpfSize(Frame.Width, Frame.Height));
            Arrange(Frame);
        }
    }

    private void Pointer(double x, double y, WasmPointerButton button, bool isDown, WasmPointerKind kind, int id = 1) =>
        _router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, button, isDown) { Kind = kind, PointerId = id });

    private void MoveTo(double x, double y) => Pointer(x, y, WasmPointerButton.None, false, WasmPointerKind.Move);

    private void Press(double x, double y) => Pointer(x, y, WasmPointerButton.Left, true, WasmPointerKind.Down);

    private void Release(double x, double y) => Pointer(x, y, WasmPointerButton.Left, false, WasmPointerKind.Up);

    [Fact]
    public void Mouse_down_up_events_carry_buttons_and_clicks()
    {
        var box = new ProbeBox(10, 10, 100, 50);
        box.Layout();
        _manager.AddRoot(box);

        var seen = new List<string>();
        box.PreviewMouseDown += (_, e) => seen.Add($"preview:{e.ChangedButton}:{e.ButtonState}:{e.ClickCount}");
        box.MouseDown += (_, e) => seen.Add($"down:{e.LeftButton}");
        box.MouseUp += (_, e) => seen.Add($"up:{e.ButtonState}:{e.ClickCount}");

        Press(30, 30);
        Assert.Equal(["preview:Left:Pressed:1", "down:Pressed"], seen);
        Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
        Assert.Equal(MouseButtonState.Released, Mouse.RightButton);

        Release(30, 30);
        Assert.Equal(["preview:Left:Pressed:1", "down:Pressed", "up:Released:1"], seen);
        Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);

        Press(30, 30);
        Release(30, 30);
        Assert.Contains("preview:Left:Pressed:2", seen);
    }

    [Fact]
    public void Handled_preview_suppresses_bubble()
    {
        var box = new ProbeBox(10, 10, 100, 50);
        box.Layout();
        _manager.AddRoot(box);

        var bubbled = 0;
        box.MouseDown += (_, _) => bubbled++;
        box.PreviewMouseDown += (_, e) => e.Handled = true;

        Press(30, 30);
        Assert.Equal(0, bubbled);
    }

    [Fact]
    public void Hover_transitions_track_the_topmost_element()
    {
        var first = new ProbeBox(0, 0, 100, 100);
        var second = new ProbeBox(200, 0, 100, 100);
        first.Layout();
        second.Layout();
        _manager.AddRoot(first);
        _manager.AddRoot(second);

        var log = new List<string>();
        first.MouseEnter += (_, _) => log.Add("enter-first");
        first.MouseLeave += (_, _) => log.Add("leave-first");
        second.MouseEnter += (_, _) => log.Add("enter-second");
        second.MouseLeave += (_, _) => log.Add("leave-second");

        MoveTo(50, 50);
        Assert.Equal(["enter-first"], log);
        Assert.True(first.IsMouseDirectlyOver);
        Assert.True(first.IsMouseOver);
        Assert.False(second.IsMouseOver);
        Assert.Same(first, Mouse.DirectlyOver);

        log.Clear();
        MoveTo(250, 50);
        Assert.Equal(["leave-first", "enter-second"], log);
        Assert.Same(second, Mouse.DirectlyOver);

        log.Clear();
        MoveTo(260, 60);
        Assert.Empty(log);

        log.Clear();
        MoveTo(500, 500);
        Assert.Equal(["leave-second"], log.Where(m => m.StartsWith("leave")).ToList());
        Assert.Null(Mouse.DirectlyOver);
    }

    [Fact]
    public void Capture_routes_around_hit_testing()
    {
        var first = new ProbeBox(0, 0, 100, 100);
        var second = new ProbeBox(200, 0, 100, 100);
        first.Layout();
        second.Layout();
        _manager.AddRoot(first);
        _manager.AddRoot(second);

        var secondMoves = 0;
        second.MouseMove += (_, _) => secondMoves++;

        MoveTo(250, 50);
        Assert.Equal(1, secondMoves);

        Assert.True(Mouse.Capture(first));
        Assert.Same(first, Mouse.Captured);
        Assert.Same(first, Mouse.PrimaryDevice.Captured);

        MoveTo(250, 50);
        Assert.Equal(1, secondMoves);

        Assert.True(Mouse.Capture(null));
        Assert.Null(Mouse.Captured);

        MoveTo(250, 50);
        Assert.Equal(2, secondMoves);

        Assert.False(Mouse.Capture(new ProbeBox(0, 0, 1, 1)));
    }

    [Fact]
    public void GetPosition_is_relative_to_the_element()
    {
        var box = new ProbeBox(10, 20, 100, 50);
        box.Layout();
        _manager.AddRoot(box);

        MoveTo(30, 40);
        Assert.Equal(new Point(20, 20), Mouse.GetPosition(box));
        Assert.Equal(new Point(30, 40), Mouse.GetPosition(null));
        Assert.Equal(new Point(30, 40), Mouse.PrimaryDevice.GetPosition(null));
    }

    [Fact]
    public void Wheel_events_carry_delta()
    {
        var box = new ProbeBox(0, 0, 400, 400);
        box.Layout();
        _manager.AddRoot(box);

        var deltas = new List<int>();
        box.PreviewMouseWheel += (_, e) => deltas.Add(e.Delta);
        _router.DispatchWheel(new WasmWheelArgs(new WasmWindowHandle(1), 50, 50, 120));
        Assert.Equal([120], deltas);
    }

    [Fact]
    public void Cursors_expose_shared_instances_and_enums_match()
    {
        Assert.NotNull(Cursors.Arrow);
        Assert.NotNull(Cursors.Hand);
        Assert.NotNull(Cursors.Cross);
        Assert.NotSame(Cursors.Arrow, Cursors.Hand);
        Assert.Equal("Wait", Cursors.Wait.ToString());

        Assert.Equal(0, (int)MouseButton.Left);
        Assert.Equal(1, (int)MouseButton.Middle);
        Assert.Equal(2, (int)MouseButton.Right);
        Assert.Equal(3, (int)MouseButton.XButton1);
        Assert.Equal(4, (int)MouseButton.XButton2);
        Assert.Equal(0, (int)MouseButtonState.Released);
        Assert.Equal(1, (int)MouseButtonState.Pressed);
    }

    [Fact]
    public void Traversal_requests_move_in_tab_order_only()
    {
        var first = new ProbeBox(0, 0, 100, 100);
        var second = new ProbeBox(0, 200, 100, 100);
        first.Layout();
        second.Layout();
        _manager.AddRoot(first);
        _manager.AddRoot(second);

        Assert.True(first.Focus());
        Assert.True(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
        Assert.Same(second, _manager.Focused);
        Assert.True(second.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
        Assert.Same(first, _manager.Focused);
        Assert.True(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Last)));
        Assert.Same(second, _manager.Focused);
        Assert.True(second.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
        Assert.Same(first, _manager.Focused);
        Assert.False(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left)));
        Assert.False(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right)));
        Assert.False(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)));
        Assert.False(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down)));

        var request = new TraversalRequest(FocusNavigationDirection.Next);
        Assert.False(request.Wrapped);
        Assert.Equal(0, (int)FocusNavigationDirection.Next);
        Assert.Equal(7, (int)FocusNavigationDirection.Down);
    }

    [Fact]
    public void Expander_toggles_on_header_click_only()
    {
        var expander = new Expander { Header = "H", Content = new TextBlock { Text = "C" } };
        expander.Measure(new WpfSize(400, 600));
        expander.Arrange(new WpfRect(0, 0, 400, 600));
        _manager.AddRoot(expander);
        var expanded = 0;
        var collapsed = 0;
        expander.Expanded += (_, _) => expanded++;
        expander.Collapsed += (_, _) => collapsed++;
        var headerY = expander.RenderRect.Y + 4;
        Press(10, headerY);
        Release(10, headerY);
        Assert.True(expander.IsExpanded);
        Assert.Equal(1, expanded);
        Assert.Equal(0, collapsed);
        expander.Measure(new WpfSize(400, 600));
        expander.Arrange(new WpfRect(0, 0, 400, 600));
        var host = Assert.IsType<StackPanel>(Assert.Single(expander.LogicalChildren));
        var content = Assert.IsType<TextBlock>(host.LogicalChildren[1]);
        var contentY = content.RenderRect.Y + 2;
        Press(10, contentY);
        Release(10, contentY);
        Assert.True(expander.IsExpanded);
        Assert.Equal(1, expanded);
        Press(10, headerY);
        Release(10, headerY);
        Assert.False(expander.IsExpanded);
        Assert.Equal(1, collapsed);
    }
}
