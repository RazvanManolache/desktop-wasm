using DesktopWasm.Core;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Live WPF-guest direction: a real engine Button/TextBox hosted in a
/// WpfGuestSession mutates actual control state through the session's
/// translated input, with explicit sizing and lifetime evidence.
/// </summary>
public sealed class WpfGuestSessionTests
{
    private static HostPointerEvent Down(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Down);

    private static HostPointerEvent Up(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Up);

    [Fact]
    public void Pointer_click_mutates_real_button_state()
    {
        var button = new Button { Content = "Go" };
        using var session = new WpfGuestSession(button, new WpfSize(800, 600));

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        // Button is arranged over the whole 800x600 session; press and
        // release at its centre in device px (scale 1 == layout px).
        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.Equal(0, clicks); // press arms, does not click
        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Pointer_down_outside_is_dropped_and_clicks_nothing()
    {
        var button = new Button { Content = "Go" };
        using var session = new WpfGuestSession(button, new WpfSize(800, 600));

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.False(session.DispatchPointer(Down(900, 300)));
        Assert.True(session.DispatchPointer(Up(900, 300)));
        Assert.Equal(0, clicks);

        // The session still works after a dropped press.
        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Keyboard_mutates_real_textbox_state()
    {
        var box = new TextBox();
        using var session = new WpfGuestSession(box, new WpfSize(800, 600));

        Assert.True(box.Focus());
        Assert.Same(box, session.Manager.Focused);
        Assert.True(box.IsKeyboardFocused);

        session.DispatchKey(new HostKeyEvent("A", IsDown: true));
        session.DispatchKey(new HostKeyEvent("A", IsDown: false));
        Assert.Equal("A", box.Text);
    }

    [Fact]
    public void Dpi_scales_input_and_frame_together()
    {
        var button = new Button { Content = "Go" };
        using var session = new WpfGuestSession(button, new WpfSize(400, 300), InteropDpi.ForScale(2));

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        // Session is 400x300 layout px at scale 2, so device (400,300)
        // lands on layout (200,150): the centre of the button.
        Assert.True(session.DispatchPointer(Down(400, 300)));
        Assert.True(session.DispatchPointer(Up(400, 300)));
        Assert.Equal(1, clicks);

        var frame = session.RenderFrame();
        Assert.Equal(800, frame.Width);
        Assert.Equal(600, frame.Height);
        Assert.Equal(2, frame.Dpi.ScaleX);
        Assert.NotEmpty(frame.Commands);
    }

    [Fact]
    public void Resize_rearranges_and_rejects_empty_size()
    {
        var button = new Button { Content = "Go" };
        using var session = new WpfGuestSession(button, new WpfSize(800, 600));

        session.Resize(new WpfSize(400, 300));
        Assert.Equal(new WpfSize(400, 300), session.Size);
        Assert.Equal(400, button.RenderSize.Width);
        Assert.Equal(300, button.RenderSize.Height);

        var frame = session.RenderFrame();
        Assert.Equal(400, frame.Width);
        Assert.Equal(300, frame.Height);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(new WpfSize(0, 300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(new WpfSize(400, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WpfGuestSession(button, new WpfSize(0, 0)));
    }

    [Fact]
    public void Dispose_is_deterministic()
    {
        var button = new Button { Content = "Go" };
        var session = new WpfGuestSession(button, new WpfSize(800, 600));

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        session.Dispose(); // second dispose is a no-op

        Assert.Throws<ObjectDisposedException>(() => session.DispatchPointer(Down(10, 10)));
        Assert.Throws<ObjectDisposedException>(() => session.DispatchKey(new HostKeyEvent("A", true)));
        Assert.Throws<ObjectDisposedException>(() => session.RenderFrame());
        Assert.Throws<ObjectDisposedException>(() => session.Resize(new WpfSize(10, 10)));
    }
}
