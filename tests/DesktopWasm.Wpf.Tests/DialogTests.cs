using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Modal dialog v1 tests: input gating to the dialog root, focus and
/// Tab containment, DialogResult/close lifecycle, re-show, host render
/// skipping hidden windows, and Window.Close parity.</summary>
[ResetAmbientState]
public sealed class DialogTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private sealed class DialogHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Main { get; } = new();
        public Button MainButton { get; } = new() { Content = "MainText" };
        public Window Dialog { get; } = new();
        public Button Ok { get; } = new() { Content = "DialogText" };
        public Button Cancel { get; } = new() { Content = "Cancel" };
        public List<string> Clicked { get; } = new();
        public List<string> Closed { get; } = new();

        public DialogHarness()
        {
            MainButton.Click += (_, _) => Clicked.Add("main");
            Ok.Click += (_, _) => Clicked.Add("ok");
            Cancel.Click += (_, _) => Clicked.Add("cancel");
            Dialog.Closed += (_, _) => Closed.Add(Dialog.DialogResult is bool b ? b.ToString() : "null");
            Main.Content = MainButton;
            var panel = new StackPanel();
            panel.AddChild(Ok);
            panel.AddChild(Cancel);
            Dialog.Content = panel;
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Main);
            Layout();
        }

        public void Layout()
        {
            WasmMilCore.RenderHeadless(Main, new WpfSize(400, 600));
            WasmMilCore.RenderHeadless(Dialog, new WpfSize(400, 600));
        }
    }

    private static WpfPoint Center(UIElement e)
    {
        var r = e.ArrangedRect;
        return new WpfPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static void ClickAt(DialogHarness h, UIElement target)
    {
        h.Layout();
        var p = Center(target);
        Down(h.Router, p.X, p.Y);
        h.Layout();
        p = Center(target);
        Up(h.Router, p.X, p.Y);
    }

    [Fact]
    public void ShowDialog_blocks_main_and_serves_dialog()
    {
        var h = new DialogHarness();
        ClickAt(h, h.MainButton);
        Assert.Equal(["main"], h.Clicked);

        h.Manager.ShowDialog(h.Dialog);
        Assert.Same(h.Dialog, h.Manager.ModalRoot);
        Assert.True(h.Dialog.IsModal);

        ClickAt(h, h.MainButton);
        Assert.Equal(["main"], h.Clicked); // blocked: no second main click
        ClickAt(h, h.Ok);
        Assert.Equal(["main", "ok"], h.Clicked);
        Assert.True(h.Dialog.IsVisible); // no auto-close magic in v1
    }

    [Fact]
    public void HitTest_stays_inside_dialog_while_modal()
    {
        var h = new DialogHarness();
        h.Manager.ShowDialog(h.Dialog);
        var hit = h.Manager.HitTest(200, 500);
        Assert.NotNull(hit);
        Assert.True(WpfInputManager.IsInSubtree(h.Dialog, hit));
    }

    [Fact]
    public void CloseDialog_records_result_restores_routing_and_fires_closed()
    {
        var h = new DialogHarness();
        h.Manager.ShowDialog(h.Dialog);
        ClickAt(h, h.MainButton);
        Assert.Equal([], h.Clicked);

        h.Manager.CloseDialog(h.Dialog, true);
        Assert.Equal(true, h.Dialog.DialogResult);
        Assert.Null(h.Manager.ModalRoot);
        Assert.False(h.Dialog.IsModal);
        Assert.False(h.Dialog.IsVisible);
        Assert.Equal(["True"], h.Closed);

        ClickAt(h, h.MainButton);
        Assert.Equal(["main"], h.Clicked);
    }

    [Fact]
    public void Close_preserves_prior_result_when_none_passed()
    {
        var h = new DialogHarness();
        h.Dialog.DialogResult = false;
        h.Manager.ShowDialog(h.Dialog);
        h.Manager.CloseDialog(h.Dialog);
        Assert.Equal(false, h.Dialog.DialogResult);
        Assert.Equal(["False"], h.Closed);
    }

    [Fact]
    public void TryFocus_refuses_outside_controls_while_modal()
    {
        var h = new DialogHarness();
        Assert.True(h.Manager.TryFocus(h.MainButton));
        h.Manager.ShowDialog(h.Dialog);
        // Showing focuses the dialog window itself.
        Assert.Same(h.Dialog, h.Manager.Focused);
        Assert.False(h.Manager.TryFocus(h.MainButton));
        Assert.True(h.Manager.TryFocus(h.Ok));
        h.Manager.CloseDialog(h.Dialog);
        Assert.True(h.Manager.TryFocus(h.MainButton));
    }

    [Fact]
    public void Tab_cycles_inside_dialog_while_modal()
    {
        var h = new DialogHarness();
        h.Manager.ShowDialog(h.Dialog);
        Assert.True(h.Manager.TryFocus(h.Ok));
        Key(h.Router, "Tab");
        Assert.Same(h.Cancel, h.Manager.Focused);
        Key(h.Router, "Tab");
        Assert.Same(h.Dialog, h.Manager.Focused); // window itself is next
        Key(h.Router, "Tab");
        Assert.Same(h.Ok, h.Manager.Focused); // wraps, never leaves the dialog
    }

    [Fact]
    public void Close_clears_focus_parked_inside_dialog()
    {
        var h = new DialogHarness();
        h.Manager.ShowDialog(h.Dialog);
        Assert.True(h.Manager.TryFocus(h.Cancel));
        h.Manager.CloseDialog(h.Dialog);
        Assert.Null(h.Manager.Focused);
    }

    [Fact]
    public void Reshow_works_after_close()
    {
        var h = new DialogHarness();
        h.Manager.ShowDialog(h.Dialog);
        h.Manager.CloseDialog(h.Dialog, true);
        Assert.False(h.Dialog.IsVisible);

        h.Manager.ShowDialog(h.Dialog);
        Assert.True(h.Dialog.IsVisible);
        Assert.Same(h.Dialog, h.Manager.ModalRoot);
        ClickAt(h, h.Ok);
        Assert.Equal(["ok"], h.Clicked);
        h.Manager.CloseDialog(h.Dialog);
        Assert.Equal(true, h.Dialog.DialogResult); // persisted until overwritten
    }

    [Fact]
    public void Window_close_unmodals_and_hides()
    {
        var h = new DialogHarness();
        var closed = 0;
        h.Dialog.Closed += (_, _) => closed++;
        h.Manager.ShowDialog(h.Dialog);
        Assert.True(h.Dialog.IsModal);

        h.Dialog.Close();
        Assert.Null(h.Manager.ModalRoot);
        Assert.False(h.Dialog.IsVisible);
        Assert.Equal(1, closed);
        ClickAt(h, h.MainButton);
        Assert.Equal(["main"], h.Clicked);
    }

    [Fact]
    public void Modeless_close_hides_and_fires_closed()
    {
        var h = new DialogHarness();
        var fired = 0;
        h.Main.Closed += (_, _) => fired++;
        h.Main.Close();
        Assert.False(h.Main.IsVisible);
        Assert.Equal(1, fired);
        Assert.Empty(h.Closed); // dialog's own Closed untouched
    }

    [Fact]
    public void Host_renders_dialog_only_while_visible()
    {
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), router);
        var main = new Window { Content = new TextBlock { Text = "MainText" } };
        var dialog = new Window { Content = new TextBlock { Text = "DialogText" } };
        host.AddWindow(main, new WpfSize(400, 600));

        var clean = host.Tick(new FrameContext(1, 16.6));
        Assert.Contains("MainText", clean.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogText", clean.Json, StringComparison.Ordinal);

        host.ShowDialog(dialog, new WpfSize(400, 600));
        var modal = host.Tick(new FrameContext(2, 33.2));
        Assert.Contains("DialogText", modal.Json, StringComparison.Ordinal);

        host.CloseDialog(dialog, true);
        Assert.Equal(true, dialog.DialogResult);
        var after = host.Tick(new FrameContext(3, 49.8));
        Assert.Contains("MainText", after.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogText", after.Json, StringComparison.Ordinal);
    }
}
