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
    public void Space_and_Enter_activate_focused_toggle_buttons()
    {
        // Native parity (gallery ButtonPage checkbox): Enter/Space activate
        // the focused ButtonBase, not just Button — a focused CheckBox flips
        // through ToggleButton.OnClick, like pointer clicks do.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var panel = new StackPanel();
        var button = new Button { Content = "Go" };
        var check = new CheckBox { Content = "Pick" };
        panel.AddChild(button);
        panel.AddChild(check);
        root.Content = panel;
        manager.AddRoot(root);
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.True(manager.TryFocus(check));
        Assert.False(check.IsChecked == true);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), " ", true, false, false, false));
        Assert.True(check.IsChecked == true);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "Enter", true, false, false, false));
        Assert.False(check.IsChecked == true);

        Assert.True(manager.TryFocus(button));
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), " ", true, false, false, false));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Enter_clicks_focused_button_and_Space_checks_radio_group()
    {
        // Completes the ButtonBase activation matrix: Enter on a plain
        // Button, Space on a RadioButton (group exclusivity preserved).
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var button = new Button { Content = "Go" };
        var first = new RadioButton { Content = "One", GroupName = "G" };
        var second = new RadioButton { Content = "Two", GroupName = "G" };
        root.AddChild(button);
        root.AddChild(first);
        root.AddChild(second);
        manager.AddRoot(root);
        var handle = new WasmWindowHandle(1);
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.True(manager.TryFocus(button));
        router.DispatchKeyboard(new WasmKeyArgs(handle, "Enter", true, false, false, false));
        Assert.Equal(1, clicks);

        Assert.True(manager.TryFocus(first));
        Assert.False(first.IsChecked == true);
        router.DispatchKeyboard(new WasmKeyArgs(handle, " ", true, false, false, false));
        Assert.True(first.IsChecked == true);
        Assert.True(manager.TryFocus(second));
        router.DispatchKeyboard(new WasmKeyArgs(handle, " ", true, false, false, false));
        Assert.True(second.IsChecked == true);
        Assert.False(first.IsChecked == true);
    }

    [Fact]
    public void Space_opens_focused_closed_combobox()
    {
        // Native ComboBox keyboard handling: Space opens a closed drop-down
        // (Escape still closes through the control path); stays open on
        // repeat Space rather than toggling.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        root.AddChild(combo);
        manager.AddRoot(root);

        Assert.True(manager.TryFocus(combo));
        Assert.False(combo.IsDropDownOpen);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), " ", true, false, false, false));
        Assert.True(combo.IsDropDownOpen);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), " ", true, false, false, false));
        Assert.True(combo.IsDropDownOpen);
    }

    [Fact]
    public void Drop_row_selects_through_overlapping_sibling()
    {
        // Real gallery ComboBoxPage shape: the open drop panel overlaps page
        // content below it. The owned-overlay hit (menu precedent) routes the
        // row click to the box instead of the covering sibling.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var grid = new Grid();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        grid.AddChild(combo);
        grid.AddChild(new TextBlock { Text = "cover" });
        manager.AddRoot(grid);
        grid.Measure(new WpfSize(400, 400));
        grid.Arrange(new WpfRect(0, 0, 400, 400));

        combo.IsDropDownOpen = true;
        var render = combo.RenderRect;
        var handle = new WasmWindowHandle(1);
        router.DispatchPointer(new WasmPointerArgs(handle, render.X + 20, render.Y + 30 + 24 + 12, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, render.X + 20, render.Y + 30 + 24 + 12, WasmPointerButton.Left, false));
        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [Fact]
    public void Escape_closes_open_combobox_and_arrows_move_selection()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.AddChild(combo);
        manager.AddRoot(root);
        var handle = new WasmWindowHandle(1);

        Assert.True(manager.TryFocus(combo));
        Assert.Equal(-1, combo.SelectedIndex);
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));
        Assert.Equal(0, combo.SelectedIndex);
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowDown", true, false, false, false));
        Assert.Equal(1, combo.SelectedIndex);
        router.DispatchKeyboard(new WasmKeyArgs(handle, "ArrowUp", true, false, false, false));
        Assert.Equal(0, combo.SelectedIndex);

        combo.IsDropDownOpen = true;
        router.DispatchKeyboard(new WasmKeyArgs(handle, "Escape", true, false, false, false));
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void Outside_press_closes_open_combobox()
    {
        // Native light-dismiss: pressing outside an open drop-down closes it
        // (focus leaves the box); the press still routes to the sibling.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        var sibling = new Button { Content = "Other", Width = 200, Height = 30 };
        root.AddChild(sibling);
        root.AddChild(new Border { Height = 120 });
        root.AddChild(combo);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 400));
        root.Arrange(new WpfRect(0, 0, 400, 400));

        // Real flow: header click opens and focuses, then an outside press
        // moves focus away, which light-dismisses the drop.
        var handle = new WasmWindowHandle(1);
        var hr = combo.RenderRect;
        router.DispatchPointer(new WasmPointerArgs(handle, hr.X + 20, hr.Y + 15, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, hr.X + 20, hr.Y + 15, WasmPointerButton.Left, false));
        Assert.True(combo.IsDropDownOpen);
        Assert.Same(combo, manager.Focused);
        var r = sibling.RenderRect;
        var sx = r.X + r.Width / 2;
        var sy = r.Y + r.Height / 2;
        Assert.IsType<Button>(manager.HitTest(sx, sy));
        router.DispatchPointer(new WasmPointerArgs(handle, sx, sy, WasmPointerButton.Left, true));
        router.DispatchPointer(new WasmPointerArgs(handle, sx, sy, WasmPointerButton.Left, false));
        Assert.Same(sibling, manager.Focused);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(-1, combo.SelectedIndex);
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
