using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class RoutedInputTests
{
    private sealed class Recorder : Canvas
    {
        public readonly List<string> Seen = new();
        public bool SwallowBubble;

        public override void OnRoutedEvent(WasmRoutedEvent e)
        {
            Seen.Add($"{GetType().Name}:{e.Name}");
            if (SwallowBubble && e.Phase == WasmRoutedPhase.Bubbling) e.Handled = true;
        }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, Window Root, Button Top, Button Bottom) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var panel = new StackPanel();
        var bottom = new Button { Content = "Bottom" };
        var top = new Button { Content = "Top" };
        panel.AddChild(bottom);
        panel.AddChild(top);
        root.Content = panel;
        manager.AddRoot(root);
        // Layout: window 800x600; panel stacks (bottom 0..32, top 32..64).
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager, root, top, bottom);
    }

    private static void LeftDown(WasmInputRouter router, WasmWindowHandle? window, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(window ?? new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true));

    private static void LeftUp(WasmInputRouter router, WasmWindowHandle? window, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(window ?? new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false));

    private static void CancelPointer(WasmInputRouter router, WasmWindowHandle? window, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(window ?? new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false)
        { Kind = WasmPointerKind.Cancel });

    [Fact]
    public void HitTest_returns_topmost_child()
    {
        var (_, manager, root, top, bottom) = Harness();
        Assert.Same(top, manager.HitTest(400, 48));
        Assert.Same(bottom, manager.HitTest(60, 16));
        // Panel is arranged full-window, so the window body still hits the panel.
        Assert.Same(root.Content, manager.HitTest(799, 599));
    }

    [Fact]
    public void Click_fires_only_on_release_inside_button()
    {
        var (router, _, _, top, bottom) = Harness();
        var topClicks = 0; var bottomClicks = 0;
        top.Click += (_, _) => topClicks++;
        bottom.Click += (_, _) => bottomClicks++;

        LeftDown(router, null, 400, 48); // press arms the button: no click yet
        Assert.Equal(0, topClicks);

        LeftUp(router, null, 400, 48); // release inside -> click
        Assert.Equal(1, topClicks);
        Assert.Equal(0, bottomClicks);

        LeftDown(router, null, 400, 48); // press again...
        LeftUp(router, null, 799, 599); // ...release outside -> no click
        Assert.Equal(1, topClicks);

        LeftDown(router, null, 400, 48);
        CancelPointer(router, null, 400, 48); // cancel disarms...
        LeftUp(router, null, 400, 48); // ...so release inside does not click
        Assert.Equal(1, topClicks);

        LeftDown(router, null, 799, 599); // press on empty body
        LeftUp(router, null, 799, 599);
        Assert.Equal(1, topClicks);
        Assert.Equal(0, bottomClicks);
    }

    [Fact]
    public void Tunnel_then_bubble_order_and_handled_stops_bubble()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Recorder();
        var child = new Recorder { SwallowBubble = true };
        root.AddChild(child);
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        // Canvas arranges children at DesiredSize (zero for empty recorders);
        // force hit-testable geometry for the route under test.
        child.Arrange(new WpfRect(0, 0, 800, 600));

        LeftDown(router, null, 10, 10);

        // Tunneling root->child, then bubbling child (which swallows) — root never sees the bubble.
        Assert.Equal(["Recorder:PreviewPointerDown", "Recorder:PointerDown"], child.Seen);
        Assert.Equal(["Recorder:PreviewPointerDown"], root.Seen);
    }

    [Fact]
    public void Keyboard_routes_to_focused_element_and_focus_follows_pointer()
    {
        var (router, manager, _, top, bottom) = Harness();
        UIElement? changedTo = null;
        manager.FocusChanged += el => changedTo = el;

        LeftDown(router, null, 60, 16); // over Bottom -> implicit focus
        Assert.Same(bottom, manager.Focused);
        Assert.Same(bottom, changedTo);

        var keys = new List<string>();
        manager.Routed += (el, e, phase) =>
        {
            if (e is WasmInputEvent.Key) keys.Add($"{el.GetType().Name}:{phase}");
        };
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "A", true, false, false, false));

        Assert.Contains("Button:Bubbling", keys);
        Assert.Equal(2, keys.Count(k => k.StartsWith("Button", StringComparison.Ordinal))); // tunnel+bubble, focused button only
        Assert.True(manager.TryFocus(top));
        Assert.Same(top, manager.Focused);
        Assert.False(manager.TryFocus(new TextBlock())); // not focusable
    }
}
