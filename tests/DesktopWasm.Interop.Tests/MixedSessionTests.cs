extern alias Fork;

using DesktopWasm.Core;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Mixed fixture: BOTH actual engine controls in one session. A WPF button
/// press mutates real WinForms control state and a WinForms button press
/// mutates real WPF state, plus joint negative lifetime/size/input cases.
/// A paintable fake, two catalog tiles or two processes would not satisfy
/// this: the events cross engine boundaries in one process, one session.
/// </summary>
public sealed class MixedSessionTests
{
    private static HostPointerEvent Down(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Down);

    private static HostPointerEvent Up(double x, double y) =>
        new(x, y, WasmPointerButton.Left, WasmPointerKind.Up);

    private sealed record Fixture(
        MixedGuestSession Session,
        Button WpfButton,
        TextBox WpfBox,
        ForkForms.Button WfButton,
        ForkForms.TextBox WfBox);

    private static Fixture Create()
    {
        var wpfButton = new Button { Content = "To WF" };
        var wpfBox = new TextBox();
        var wpf = new WpfGuestSession(wpfButton, new WpfSize(800, 600));

        var wfButton = new ForkForms.Button { Text = "To WPF" };
        var wfBox = new ForkForms.TextBox();
        var wf = new WinFormsGuestSession(wfButton, new WpfSize(800, 600));

        // Cross-engine wiring: each engine's real event mutates the other
        // engine's real control state.
        wpfButton.Click += (_, _) => wfBox.Text = "from-wpf";
        wfButton.Click += (_, _) => wpfBox.Text = "from-wf";

        return new Fixture(new MixedGuestSession(wpf, wf), wpfButton, wpfBox, wfButton, wfBox);
    }

    [Fact]
    public void Wpf_press_mutates_real_winforms_state()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.True(f.Session.DispatchPointerToWpf(Down(400, 300)));
            Assert.True(f.Session.DispatchPointerToWpf(Up(400, 300)));
            Assert.Equal("from-wpf", f.WfBox.Text);
        }
    }

    [Fact]
    public void WinForms_press_mutates_real_wpf_state()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.True(f.Session.DispatchPointerToWinForms(Down(400, 300)));
            Assert.True(f.Session.DispatchPointerToWinForms(Up(400, 300)));
            Assert.Equal("from-wf", f.WpfBox.Text);
        }
    }

    [Fact]
    public void Outside_press_is_dropped_on_both_halves()
    {
        var f = Create();
        using (f.Session)
        {
            Assert.False(f.Session.DispatchPointerToWpf(Down(900, 300)));
            Assert.False(f.Session.DispatchPointerToWinForms(Down(900, 300)));
            Assert.True(f.Session.DispatchPointerToWpf(Up(900, 300)));
            Assert.True(f.Session.DispatchPointerToWinForms(Up(900, 300)));

            Assert.Equal(string.Empty, f.WpfBox.Text);
            Assert.Equal(string.Empty, f.WfBox.Text);
        }
    }

    [Fact]
    public void RenderBoth_reports_device_frames_for_each_half()
    {
        // Rasterizable halves (a text-painting WinForms guest cannot rasterize
        // off-browser; that limit is pinned in WinFormsGuestSessionTests).
        using var wpf = new WpfGuestSession(new Button(), new WpfSize(800, 600));
        using var wf = new WinFormsGuestSession(new ForkForms.Panel(), new WpfSize(400, 300));
        using var session = new MixedGuestSession(wpf, wf);

        var (wpfFrame, wfFrame) = session.RenderBoth();
        Assert.Equal(800, wpfFrame.Width);
        Assert.Equal(600, wpfFrame.Height);
        Assert.Equal(400, wfFrame.Width);
        Assert.Equal(300, wfFrame.Height);
    }

    [Fact]
    public void Dispose_disposes_both_halves_deterministically()
    {
        var f = Create();
        var session = f.Session;

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.DispatchPointerToWpf(Down(10, 10)));
        Assert.Throws<ObjectDisposedException>(() => session.DispatchPointerToWinForms(Down(10, 10)));
        Assert.Throws<ObjectDisposedException>(() => session.DispatchKeyToWpf(new HostKeyEvent("A", true)));
        Assert.Throws<ObjectDisposedException>(() => session.DispatchKeyToWinForms(new HostKeyEvent("A", true)));
        Assert.Throws<ObjectDisposedException>(() => session.RenderBoth());
    }

    [Fact]
    public void Null_half_is_rejected()
    {
        var wpf = new WpfGuestSession(new Button(), new WpfSize(10, 10));
        var wf = new WinFormsGuestSession(new ForkForms.Button(), new WpfSize(10, 10));
        try
        {
            Assert.Throws<ArgumentNullException>(() => new MixedGuestSession(null!, wf));
            Assert.Throws<ArgumentNullException>(() => new MixedGuestSession(wpf, null!));
        }
        finally
        {
            wpf.Dispose();
            wf.Dispose();
        }
    }
}

