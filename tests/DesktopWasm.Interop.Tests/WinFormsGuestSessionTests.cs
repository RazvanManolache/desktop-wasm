extern alias Fork;
extern alias WasmDraw;

using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WfColor = WasmDraw::System.Drawing.Color;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Live WinForms-guest direction: a real fork control hosted in a
/// WinFormsGuestSession mutates actual control state through the session's
/// translated input, with explicit focus/capture/tab/lifetime evidence.
/// </summary>
public sealed class WinFormsGuestSessionTests
{
    private static HostPointerEvent Down(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Down);

    private static HostPointerEvent Up(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Up);

    [Fact]
    public void Pointer_click_mutates_real_button_state()
    {
        var button = new ForkForms.Button { Text = "Go" };
        using var session = new WinFormsGuestSession(button, new WpfSize(800, 600));

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.Equal(0, clicks); // press arms, does not click
        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Pointer_down_outside_is_dropped_and_clicks_nothing()
    {
        var button = new ForkForms.Button { Text = "Go" };
        using var session = new WinFormsGuestSession(button, new WpfSize(800, 600));

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.False(session.DispatchPointer(Down(900, 300)));
        Assert.True(session.DispatchPointer(Up(900, 300)));
        Assert.Equal(0, clicks);

        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Press_holds_real_capture_and_focus_until_release()
    {
        var button = new ForkForms.Button { Text = "Go" };
        using var session = new WinFormsGuestSession(button, new WpfSize(800, 600));

        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.Same(button, session.PressedControl);
        Assert.True(button.Capture);
        Assert.True(button.Focused);

        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Null(session.PressedControl);
        Assert.False(button.Capture);
    }

    [Fact]
    public void Keyboard_reaches_focused_control_with_real_keycode()
    {
        var box = new ForkForms.TextBox { Text = "hello" };
        using var session = new WinFormsGuestSession(box, new WpfSize(800, 600));

        Assert.True(session.DispatchPointer(Down(10, 10)));
        Assert.True(session.DispatchPointer(Up(10, 10)));
        Assert.True(box.Focused);

        ForkForms.Keys? seen = null;
        box.KeyDown += (_, e) => seen = e.KeyCode;
        session.DispatchKey(new HostKeyEvent("B", IsDown: true));
        session.DispatchKey(new HostKeyEvent("B", IsDown: false));
        Assert.Equal(ForkForms.Keys.B, seen);
    }

    [Fact]
    public void CtrlA_selects_all_text_like_production_host()
    {
        var box = new ForkForms.TextBox { Text = "hello" };
        using var session = new WinFormsGuestSession(box, new WpfSize(800, 600));

        Assert.True(box.Focus());
        session.DispatchKey(new HostKeyEvent("A", IsDown: true, Ctrl: true));
        session.DispatchKey(new HostKeyEvent("A", IsDown: false, Ctrl: true));
        Assert.Equal(5, box.SelectionLength);
        Assert.Equal("hello", box.SelectedText);
    }

    [Fact]
    public void Tab_moves_focus_across_real_buttons()
    {
        using var form = new ForkForms.Form { Text = "Tab" };
        var first = new ForkForms.Button { Text = "First" };
        var second = new ForkForms.Button { Text = "Second" };
        form.Controls.Add(first);
        form.Controls.Add(second);
        using var session = new WinFormsGuestSession(form, new WpfSize(800, 600));

        Assert.True(first.Focus());
        Assert.Same(first, form.ActiveControl);

        session.DispatchKey(new HostKeyEvent("Tab", IsDown: true));
        session.DispatchKey(new HostKeyEvent("Tab", IsDown: false));
        Assert.Same(second, form.ActiveControl);

        session.DispatchKey(new HostKeyEvent("Tab", IsDown: true, Shift: true));
        session.DispatchKey(new HostKeyEvent("Tab", IsDown: false, Shift: true));
        Assert.Same(first, form.ActiveControl);
    }

    [Fact]
    public void Resize_resizes_control_and_rejects_empty_size()
    {
        var button = new ForkForms.Button { Text = "Go" };
        using var session = new WinFormsGuestSession(button, new WpfSize(800, 600));

        session.Resize(new WpfSize(400, 300));
        Assert.Equal(new WpfSize(400, 300), session.Size);
        Assert.Equal(400, button.Width);
        Assert.Equal(300, button.Height);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(new WpfSize(0, 300)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WinFormsGuestSession(button, new WpfSize(-1, 10)));
    }

    [Fact]
    public void Dispose_is_deterministic()
    {
        var button = new ForkForms.Button { Text = "Go" };
        var session = new WinFormsGuestSession(button, new WpfSize(800, 600));

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.DispatchPointer(Down(10, 10)));
        Assert.Throws<ObjectDisposedException>(() => session.DispatchKey(new HostKeyEvent("A", true)));
        Assert.Throws<ObjectDisposedException>(() => session.RenderFrame());
    }

    [Fact]
    public void RenderFrame_rasterizes_panel_to_texture_command()
    {
        var panel = new ForkForms.Panel { BackColor = WfColor.Red };
        using var session = new WinFormsGuestSession(panel, new WpfSize(40, 30));

        var frame = session.RenderFrame();
        Assert.Equal(40, frame.Width);
        Assert.Equal(30, frame.Height);

        var image = Assert.Single(frame.Commands);
        var draw = Assert.IsType<DrawCommand.DrawImage>(image);
        Assert.Equal(40 * 30 * 4, draw.Rgba.Length);
        // Opaque red fill: first pixel is full-alpha red.
        Assert.Equal(255, draw.Rgba[0]);
        Assert.Equal(0, draw.Rgba[1]);
        Assert.Equal(0, draw.Rgba[2]);
        Assert.Equal(255, draw.Rgba[3]);
    }

    [Fact]
    public void RenderFrame_button_text_throws_offbrowser_with_browser_oracle()
    {
        // Inherited drawing-layer limit (same contract TextBridgeTests pins):
        // text rasterization needs the browser canvas bridge, so a
        // text-painting guest cannot rasterize in the desktop test process.
        // The session surfaces the real cause, not a reflection wrapper.
        var button = new ForkForms.Button { Text = "Go" };
        using var session = new WinFormsGuestSession(button, new WpfSize(120, 40));

        var ex = Assert.Throws<NotSupportedException>(() => session.RenderFrame());
        Assert.Contains("browser canvas bridge", ex.Message, StringComparison.Ordinal);
    }
}
